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
        private void HandleAnyInteractableTriggered(TacticalInteractable prop, TacticalUnit unit)
        {
            if (IsInExploration || IsCombatOver || prop == null) return;
            if (unit != null && !_allUnits.Contains(unit)) return;

            bool updated = false;
            for (int i = 0; i < _objectives.Count; i++)
            {
                var obj = _objectives[i];
                if (obj == null || obj.Status != CombatObjectiveStatus.InProgress) continue;
                if (obj.Type == CombatObjectiveType.HackTerminal)
                {
                    if (obj.TargetInteractable == prop 
                        || (!string.IsNullOrEmpty(obj.TargetInteractableId) && (string.Equals(prop.ObjectName, obj.TargetInteractableId, StringComparison.OrdinalIgnoreCase) || string.Equals(prop.name, obj.TargetInteractableId, StringComparison.OrdinalIgnoreCase)))
                        || (obj.TargetInteractable == null && string.IsNullOrEmpty(obj.TargetInteractableId)))
                    {
                        obj.RegisterTerminalHacked();
                        updated = true;
                        OnObjectiveUpdated?.Invoke(obj);
                        if (obj.IsCompleted)
                        {
                            OnObjectiveCompleted?.Invoke(obj);
                        }
                    }
                }
            }
            if (updated)
            {
                CheckCombatOver();
            }
        }

        private void HandleAnyUnitMoveCompleted(TacticalUnit unit)
        {
            if (IsInExploration || IsCombatOver || unit == null) return;
            if (!_allUnits.Contains(unit)) return;
            CheckCombatOver();
        }

        public void SetExplorationMode(bool active, TacticalUnit defaultUnit = null)
        {
            var ai = FindAnyObjectByType<Killtime.Tactics.AI.TacticalAIController>();

            if (active)
            {
                SystemMode = TurnSystemMode.Exploration;
                IsCombatOver = false;
                CurrentOutcome = CombatOutcome.InProgress;

                if (ai != null)
                {
                    ai.StopAITurn();
                }

                for (int i = 0; i < _allUnits.Count; i++)
                {
                    var u = _allUnits[i];
                    if (u != null && u.Stats != null)
                    {
                        u.Stats.CurrentActionPoints = u.Stats.MaxActionPoints;
                    }
                }

                if (defaultUnit != null && _allUnits.Contains(defaultUnit))
                {
                    ActiveUnit = defaultUnit;
                }
                else if (ActiveUnit == null && _allUnits.Count > 0)
                {
                    ActiveUnit = _allUnits.Find(u => u != null && u.IsPlayerControlled) ?? _allUnits[0];
                }

                if (ActiveUnit != null)
                {
                    OnTurnStarted?.Invoke(ActiveUnit);
                }

                if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                {
                    Killtime.Audio.KilltimeAudioManager.Instance.PlayMusic(Killtime.Audio.MusicMood.Explore, Killtime.Audio.MusicIntensity.Calm, true);
                }
            }
            else
            {
                EnterCombatMode();
            }
        }

        public void EnterCombatMode(TacticalUnit triggeringUnit = null)
        {
            if (SystemMode == TurnSystemMode.CombatTurnBased && !IsCombatOver) return;

            SystemMode = TurnSystemMode.CombatTurnBased;
            IsCombatOver = false;
            CurrentOutcome = CombatOutcome.InProgress;
            CombatEndReason = "";
            ResetObjectivesState();
            _needsInitiativeRoll = true;
            CurrentRound = 1;
            ResetDuoTechTracking();

            // Champs de force portés : barrières rechargées en début de combat (Livre VII §28.2).
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u != null && u.Stats != null) u.Stats.RefillShield();
            }

            var ai = FindAnyObjectByType<Killtime.Tactics.AI.TacticalAIController>();
            if (ai != null)
            {
                ai.enabled = true;
                ai.LoadDevUIPrefs();
            }

            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
            {
                Killtime.Audio.KilltimeAudioManager.Instance.Play(Killtime.Audio.SoundId.Round_Start, 0.9f);
                Killtime.Audio.KilltimeAudioManager.Instance.PlayMusic(Killtime.Audio.MusicMood.Combat, Killtime.Audio.MusicIntensity.Intense, true);
            }

            StartNewRound();

            if (triggeringUnit != null && _allUnits.Contains(triggeringUnit))
            {
                int triggerIdx = _allUnits.IndexOf(triggeringUnit);
                if (triggerIdx >= 0)
                {
                    _activeUnitIndex = triggerIdx;
                    ActiveUnit = triggeringUnit;
                    OnTurnStarted?.Invoke(ActiveUnit);
                }
            }

            if (ActiveUnit != null)
            {
                ai?.TriggerAITurnIfApplicable();
            }
        }

        public void SetActiveUnitExplicit(TacticalUnit unit)
        {
            if (unit == null || !_allUnits.Contains(unit))
            {
                // Refus silencieux avant : une sélection impossible (acteur non
                // enregistré) ne laissait aucune trace — d'où des clics "morts".
                string label = unit == null ? "(détruite ou null)" : unit.name;
                Debug.LogWarning($"[TurnManager] Sélection refusée pour '{label}' : unité non enregistrée au tour.");
                return;
            }
            ActiveUnit = unit;
            _activeUnitIndex = _allUnits.IndexOf(unit);
            OnTurnStarted?.Invoke(ActiveUnit);
        }

        public void RegisterUnit(TacticalUnit unit)
        {
            if (unit == null || _allUnits.Contains(unit)) return;

            IntroduceUnitToRegisteredUnits(unit);

            _allUnits.Add(unit);

            if (unit.Stats != null && unit.Stats.IsAlive)
            {
                unit.Stats.ResetTurn();
            }

            if (IsMultiplayerPlayerClient())
            {
                return;
            }

            if (IsInExploration)
            {
                if (ActiveUnit == null && unit.IsPlayerControlled)
                {
                    ActiveUnit = unit;
                    OnTurnStarted?.Invoke(ActiveUnit);
                }
                return;
            }

            if (IsCombatOver)
            {
                IsCombatOver = false;
                CurrentOutcome = CombatOutcome.InProgress;
                CurrentRound = 1;
                _needsInitiativeRoll = true;
                if (!SuspendAutoStart) StartNewRound();
                return;
            }

            if (ActiveUnit == null || !_allUnits.Contains(ActiveUnit))
            {
                if (!SuspendAutoStart) StartNewRound();
                return;
            }

            InsertUnitIntoCurrentRound(unit);
        }   

        private void IntroduceUnitToRegisteredUnits(TacticalUnit newUnit)
        {
            if (newUnit == null) return;
            var sheetNew = newUnit.GetOrBuildSheet();
            if (sheetNew == null) return;

            string location = "Terrain Tactique";
            var arena = FindAnyObjectByType<CombatDevArena>();
            if (arena != null && arena.HasLoadedMap && arena.CurrentLoadedMap != null)
            {
                location = arena.CurrentLoadedMap.MapName;
            }
            else if (arena != null)
            {
                location = $"Arène ({arena.CurrentLayout})";
            }

            string context = IsInExploration ? "Exploration" : $"Engagement Tactique (Round {CurrentRound})";

            for (int i = 0; i < _allUnits.Count; i++)
            {
                var existing = _allUnits[i];
                if (existing == null || existing == newUnit) continue;
                var sheetExisting = existing.GetOrBuildSheet();
                if (sheetExisting == null) continue;

                RelationshipLinkType link = ResolveInitialRelationshipLink(newUnit, existing);
                CharacterRelationshipStorageService.IntroduceCharacters(sheetNew, sheetExisting, link, location, context);
            }
        }

        private RelationshipLinkType ResolveInitialRelationshipLink(TacticalUnit a, TacticalUnit b)
        {
            var sheetA = a.GetOrBuildSheet();
            var sheetB = b.GetOrBuildSheet();

            bool isHeroicTrioA = MinaCharacter.IsMina(sheetA) || LucasCharacter.IsLucas(sheetA) || ThomasCharacter.IsThomas(sheetA) || JohnCharacter.IsJohn(sheetA) || ErikaCharacter.IsErika(sheetA) || VanceCharacter.IsVance(sheetA);
            bool isHeroicTrioB = MinaCharacter.IsMina(sheetB) || LucasCharacter.IsLucas(sheetB) || ThomasCharacter.IsThomas(sheetB) || JohnCharacter.IsJohn(sheetB) || ErikaCharacter.IsErika(sheetB) || VanceCharacter.IsVance(sheetB);

            if (isHeroicTrioA && isHeroicTrioB)
            {
                return RelationshipLinkType.SymbioteLigneZero;
            }

            if (a.IsPlayerControlled == b.IsPlayerControlled)
            {
                return a.IsPlayerControlled ? RelationshipLinkType.FraterniteDarmes : RelationshipLinkType.HierarchieMilitaire;
            }

            return RelationshipLinkType.HostiliteDeclaree;
        }

        private void InsertUnitIntoCurrentRound(TacticalUnit newUnit)
        {
            int currentIndex = _allUnits.IndexOf(ActiveUnit);
            if (currentIndex < 0) return;

            // Renfort mid-combat (choix mixte) : pas de re-jet général.
            // On roule l'initiative du nouvel arrivant puis on l'insère parmi les
            // unités restantes selon (initiative, puis RAP en tie-break).
            EnsureInitiativeFor(newUnit);

            int remainingStart = currentIndex + 1;
            int remainingCount = _allUnits.Count - remainingStart;

            if (remainingCount > 1)
            {
                _allUnits.Sort(remainingStart, remainingCount, Comparer<TacticalUnit>.Create((a, b) =>
                {
                    int initA = GetInitiativeTotal(a);
                    int initB = GetInitiativeTotal(b);
                    if (initA != initB) return initB.CompareTo(initA);
                    int rapA = (a != null && a.Stats != null) ? a.Stats.Attributes.Rapidite : 0;
                    int rapB = (b != null && b.Stats != null) ? b.Stats.Attributes.Rapidite : 0;
                    return rapB.CompareTo(rapA);
                }));
            }

            _activeUnitIndex = _allUnits.IndexOf(ActiveUnit);
        }

        public void ClearUnits()
        {
            _allUnits.Clear();
            ActiveUnit = null;
            _activeUnitIndex = 0;
            _needsInitiativeRoll = true;
        }

        public void UnregisterUnit(TacticalUnit unit)
        {
            if (unit == null) return;

            // RD-030 : l'unité qui quitte la carte perd son guet.
            if (unit.Stats != null) Killtime.Core.Combat.OverwatchState.Cancel(unit.Stats);
            // RD-031 : ... et sa posture/réaction/décrochage d'opportunité.
            if (unit.Stats != null) Killtime.Core.Combat.OpportunityState.RemoveUnit(unit.Stats);
            // RD-048 : ... et son attente/retard (tenir/retarder) + sa canalisation.
            if (unit.Stats != null) Killtime.Core.Combat.DelayedTurnState.RemoveUnit(unit.Stats);
            if (unit.Stats != null) Killtime.Core.Combat.ChannelingState.RemoveUnit(unit.Stats);
            // RD-202 : ... et sa préparation one-shot / fenêtre de dégagement.
            if (unit.Stats != null) Killtime.Core.Combat.PreparationState.RemoveUnit(unit.Stats);
            if (unit.Stats != null) Killtime.Core.Combat.GetAwayState.RemoveUnit(unit.Stats);
            // RD-039 : ... et toute prise de lutte active.
            if (unit.Stats != null) Killtime.Core.Combat.GrappleState.ReleaseGrapple(unit.Stats);
            // RD-204 : ... et toute possession Esprit active.
            if (unit.Stats != null) Killtime.Core.Combat.PossessionState.ReleasePossession(unit.Stats);
            // RD-038 : ... et ses états de charge et de sprint.
            if (unit.Stats != null) Killtime.Core.Combat.ChargeState.Cancel(unit.Stats);
            // RD-049 : ... et son état de furtivité.
            if (unit.Stats != null) Killtime.Core.Combat.StealthState.Cancel(unit.Stats);
            // RD-084 : ... et toute poursuite / assaut groupé le concernant.
            if (unit.Stats != null) Killtime.Core.Combat.PursuitState.RemoveUnit(unit.Stats);
            if (unit.Stats != null) Killtime.Core.Combat.GroupAssaultState.RemoveUnit(unit.Stats);

            bool wasActive = (ActiveUnit == unit);
            int idx = _allUnits.IndexOf(unit);
            if (idx >= 0)
            {
                _allUnits.RemoveAt(idx);
            }

            if (CheckCombatOver()) return;

            if (wasActive)
            {
                _activeUnitIndex = idx;
                while (_activeUnitIndex < _allUnits.Count
                    && (_allUnits[_activeUnitIndex] == null
                        || _allUnits[_activeUnitIndex].Stats == null
                        || !_allUnits[_activeUnitIndex].Stats.IsAlive))
                {
                    _activeUnitIndex++;
                }

                if (_activeUnitIndex >= _allUnits.Count)
                {
                    CurrentRound++;
                    StartNewRound();
                }
                else
                {
                    StartUnitTurn();
                }
            }
            else if (ActiveUnit != null)
            {
                _activeUnitIndex = _allUnits.IndexOf(ActiveUnit);
            }
        }

        public void ResetCombatState()
        {
            IsCombatOver = false;
            CurrentOutcome = CombatOutcome.InProgress;
            CombatEndReason = "";
            ResetObjectivesState();
            CurrentRound = 1;
            _activeUnitIndex = 0;
            _allUnits.RemoveAll(u => u == null);
            _needsInitiativeRoll = true;
            SystemMode = TurnSystemMode.Exploration;
            ResetDuoTechTracking();
            // RD-030 : aucun guet ne survit à un reset de combat.
            Killtime.Core.Combat.OverwatchState.ClearAll();
            // RD-031 : aucune posture/réaction/décrochage ne survit non plus.
            Killtime.Core.Combat.OpportunityState.ClearAll();
            // RD-048 : aucune attente/retard ni canalisation ne survit non plus.
            Killtime.Core.Combat.DelayedTurnState.ClearAll();
            Killtime.Core.Combat.ChannelingState.ClearAll();
            // RD-202 : aucune préparation one-shot ni fenêtre de dégagement ne survit non plus.
            Killtime.Core.Combat.PreparationState.ClearAll();
            Killtime.Core.Combat.GetAwayState.ClearAll();
            // RD-039 : aucune prise de lutte ne survit à un reset de combat.
            Killtime.Core.Combat.GrappleState.ClearAll();
            // RD-204 : aucune possession Esprit ne survit à un reset de combat.
            Killtime.Core.Combat.PossessionState.ClearAll();
            // RD-038 : purge des états de charge et de sprint.
            Killtime.Core.Combat.ChargeState.ClearAll();
            // RD-049 : purge des états de furtivité au reset de combat.
            Killtime.Core.Combat.StealthState.ClearAll();
            // RD-084 : purge des poursuites cumulatives et des assauts synchronisés.
            Killtime.Core.Combat.PursuitState.ClearAll();
            Killtime.Core.Combat.GroupAssaultState.ClearAll();
            // RD-051 : aucun véhicule ni embarquement ne survit à un reset de combat.
            Killtime.Core.Combat.VehicleState.ClearAll();
            // Nouveau combat (ré)initialisé : aucun objet au sol (gourdin...) ne survit.
            try { DroppedWeaponPickup.ClearAllDropped(); } catch { }
        }

        /// <summary>
        /// Nouveau combat = compteurs Duo-Tech remis à zéro (limite "1 duo par
        /// personnage par round" repart à chaque affrontement).
        /// </summary>
        private void ResetDuoTechTracking()
        {
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u != null && u.Stats != null)
                    u.Stats.LastDuoTechRound = 0;
            }
        }
    }
}

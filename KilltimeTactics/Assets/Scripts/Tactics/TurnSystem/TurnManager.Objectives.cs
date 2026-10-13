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
        public bool CheckCombatOver()
        {
            if (IsInExploration) return false;
            if (IsCombatOver) return true;
            if (_allUnits.Count == 0) return false;

            int playerUnitsAlive = 0;
            int enemyUnitsAlive = 0;

            foreach (var unit in _allUnits)
            {
                if (unit == null || unit.Stats == null) continue;

                if (unit.Stats.IsAlive)
                {
                    if (unit.IsPlayerControlled)
                        playerUnitsAlive++;
                    else
                        enemyUnitsAlive++;
                }
            }

            // Cas d'anéantissement de l'escouade : Défaite absolue dans tous les modes
            if (playerUnitsAlive == 0)
            {
                EndCombatWithOutcome(CombatOutcome.Defeat, "Tous les membres de l'escouade sont hors de combat (Anéantissement).");
                return true;
            }

            // Si aucun objectif personnalisé n'est configuré : logique classique Kill-All (Livre I §4)
            if (!HasCustomObjectives)
            {
                if (enemyUnitsAlive == 0)
                {
                    EndCombatWithOutcome(CombatOutcome.Victory, "Toutes les unités ennemies ont été neutralisées.");
                    return true;
                }
                return false;
            }

            // Évaluation des objectifs personnalisés (RD-050)
            EvaluateObjectives(playerUnitsAlive, enemyUnitsAlive);

            // 1. Échec critique : un objectif obligatoire a échoué (ex: VIP mort)
            for (int i = 0; i < _objectives.Count; i++)
            {
                var obj = _objectives[i];
                if (obj == null) continue;
                if (!obj.IsOptional && obj.Status == CombatObjectiveStatus.Failed)
                {
                    string reason = !string.IsNullOrEmpty(obj.FailureReason)
                        ? obj.FailureReason
                        : $"Échec de l'objectif critique : {obj.Title}.";
                    EndCombatWithOutcome(CombatOutcome.Defeat, reason);
                    return true;
                }
            }

            // 2. Victoire par objectifs : TOUS les objectifs obligatoires sont complétés
            bool allRequiredCompleted = true;
            int requiredCount = 0;
            for (int i = 0; i < _objectives.Count; i++)
            {
                var obj = _objectives[i];
                if (obj == null) continue;
                if (!obj.IsOptional)
                {
                    requiredCount++;
                    if (obj.Status != CombatObjectiveStatus.Completed)
                    {
                        allRequiredCompleted = false;
                        break;
                    }
                }
            }

            if (requiredCount > 0 && allRequiredCompleted)
            {
                EndCombatWithOutcome(CombatOutcome.Victory, BuildObjectivesVictorySummary());
                return true;
            }

            // 3. Fallback Annihilation : si tous les ennemis sont morts et que le repli
            // par annihilation est autorisé (par défaut oui)
            if (enemyUnitsAlive == 0 && _allowAnnihilationFallback)
            {
                for (int i = 0; i < _objectives.Count; i++)
                {
                    var obj = _objectives[i];
                    if (obj != null && obj.Status == CombatObjectiveStatus.InProgress)
                    {
                        if (obj.Type == CombatObjectiveType.Annihilation || obj.Type == CombatObjectiveType.EscortVIP)
                        {
                            obj.MarkCompleted();
                            OnObjectiveCompleted?.Invoke(obj);
                        }
                    }
                }
                EndCombatWithOutcome(CombatOutcome.Victory, "Zone pacifiée : toutes les menaces ont été neutralisées.");
                return true;
            }

            return false;
        }

        private void EvaluateObjectives(int playerUnitsAlive, int enemyUnitsAlive)
        {
            for (int i = 0; i < _objectives.Count; i++)
            {
                var obj = _objectives[i];
                if (obj == null) continue;
                var prevStatus = obj.Status;
                obj.Evaluate(this, playerUnitsAlive, enemyUnitsAlive);
                if (obj.Status != prevStatus)
                {
                    OnObjectiveUpdated?.Invoke(obj);
                    if (obj.Status == CombatObjectiveStatus.Completed)
                        OnObjectiveCompleted?.Invoke(obj);
                    else if (obj.Status == CombatObjectiveStatus.Failed)
                        OnObjectiveFailed?.Invoke(obj);
                }
            }
        }

        private void NotifyAudioBattleState()
        {
            if (IsInExploration || IsCombatOver) return;
            var audioMgr = Killtime.Audio.KilltimeAudioManager.Instance;
            if (audioMgr == null) return;

            int alliesAlive = 0;
            int enemiesAlive = 0;
            int totalMaxHp = 0;
            int totalCurrentHp = 0;

            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null || u.Stats == null) continue;

                if (u.IsPlayerControlled)
                {
                    totalMaxHp += u.Stats.MaxHealth;
                    if (u.Stats.IsAlive)
                    {
                        alliesAlive++;
                        totalCurrentHp += Mathf.Max(0, u.Stats.CurrentHealth);
                    }
                }
                else if (u.Stats.IsAlive)
                {
                    enemiesAlive++;
                }
            }

            float allyHpRatio = totalMaxHp > 0 ? (float)totalCurrentHp / totalMaxHp : 0f;
            audioMgr.NotifyBattleState(allyHpRatio, alliesAlive, enemiesAlive, CurrentRound);
        }

        public void EndCombatWithOutcome(CombatOutcome outcome, string reason)
        {
            if (IsCombatOver) return;

            IsCombatOver = true;
            CurrentOutcome = outcome;
            CombatEndReason = reason ?? "";

            var audioMgr = Killtime.Audio.KilltimeAudioManager.Instance;
            if (audioMgr != null)
            {
                if (outcome == CombatOutcome.Victory)
                {
                    audioMgr.PlayStinger(Killtime.Audio.MusicMood.Victory);
                    audioMgr.PlayMusic(Killtime.Audio.MusicMood.Victory, Killtime.Audio.MusicIntensity.Intense, forceRestart: true);
                }
                else if (outcome == CombatOutcome.Defeat)
                {
                    audioMgr.PlayStinger(Killtime.Audio.MusicMood.Defeat);
                    audioMgr.PlayMusic(Killtime.Audio.MusicMood.Defeat, Killtime.Audio.MusicIntensity.Calm, forceRestart: true);
                }
            }

            OnCombatEnded?.Invoke(CurrentOutcome);

            if (IsMultiplayerGM())
            {
                var payload = new Killtime.Multi.VTTTurnControlPayload
                {
                    action = "combat_ended",
                    round = CurrentRound,
                    outcome = CurrentOutcome.ToString()
                };
                Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
            }
        }

        private string BuildObjectivesVictorySummary()
        {
            var completedTitles = new List<string>();
            for (int i = 0; i < _objectives.Count; i++)
            {
                var obj = _objectives[i];
                if (obj != null && obj.Status == CombatObjectiveStatus.Completed && !obj.IsOptional)
                {
                    completedTitles.Add(obj.Title);
                }
            }

            if (completedTitles.Count == 1)
            {
                return $"Objectif accompli avec succès : {completedTitles[0]}.";
            }
            if (completedTitles.Count > 1)
            {
                return $"Tous les objectifs prioritaires ont été accomplis ({string.Join(", ", completedTitles)}).";
            }
            return "Engagement remporté : objectifs accomplis.";
        }

        public void AddObjective(CombatObjective objective)
        {
            if (objective == null || _objectives.Contains(objective)) return;
            _objectives.Add(objective);
            OnObjectiveUpdated?.Invoke(objective);
        }

        public bool RemoveObjective(CombatObjective objective)
        {
            if (objective == null) return false;
            return _objectives.Remove(objective);
        }

        public void ClearObjectives()
        {
            _objectives.Clear();
        }

        public void ResetObjectivesState()
        {
            for (int i = 0; i < _objectives.Count; i++)
            {
                _objectives[i]?.ResetState();
            }
        }

        public void SetupAnnihilationPreset()
        {
            ClearObjectives();
            AddObjective(CombatObjective.CreateAnnihilation());
        }

        public void SetupExtractionPreset(IEnumerable<HexCoordinates> exitZone, bool requireAllLiving = true)
        {
            ClearObjectives();
            AddObjective(CombatObjective.CreateExtraction(exitZone, requireAllLiving));
        }

        public void SetupSurvivePreset(int roundsToSurvive)
        {
            ClearObjectives();
            AddObjective(CombatObjective.CreateSurvive(CurrentRound + roundsToSurvive - 1));
        }

        public void SetupEscortPreset(TacticalUnit vip, IEnumerable<HexCoordinates> destination = null)
        {
            ClearObjectives();
            AddObjective(CombatObjective.CreateEscort(vip, destination));
        }

        public void SetupHackTerminalPreset(TacticalInteractable terminal = null, string terminalId = null)
        {
            ClearObjectives();
            if (terminal != null)
                AddObjective(CombatObjective.CreateHackTerminal(terminal));
            else
                AddObjective(CombatObjective.CreateHackTerminalById(terminalId ?? "Console_Terminal"));
        }
    }
}

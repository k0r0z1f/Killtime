using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.Tactics.Objectives
{
    public enum CombatObjectiveType
    {
        Annihilation,     // Neutraliser tous les ennemis (Défaut)
        Extraction,       // Évacuer l'escouade vers la zone d'extraction
        SurviveRounds,    // Tenir X rounds face à l'ennemi
        EscortVIP,        // Protéger et/ou escorter un VIP vivant
        HackTerminal      // Pirater une console/terminal interactif
    }

    public enum CombatObjectiveStatus
    {
        InProgress,
        Completed,
        Failed
    }

    /// <summary>
    /// RD-050 : Objectifs tactiques autres que kill-all (Extraction, Survie, Escorte, Piratage).
    /// Gère les conditions de victoire/défaite, le suivi de progression et les alertes.
    /// </summary>
    [Serializable]
    public class CombatObjective
    {
        [SerializeField] private string _id = Guid.NewGuid().ToString("N")[..8];
        [SerializeField] private string _title = "Objectif";
        [SerializeField] private string _description = "";
        [SerializeField] private CombatObjectiveType _type = CombatObjectiveType.Annihilation;
        [SerializeField] private bool _isOptional = false;
        [SerializeField] private CombatObjectiveStatus _status = CombatObjectiveStatus.InProgress;
        [SerializeField] private string _failureReason = "";

        // Paramètres Extraction
        [SerializeField] private List<HexCoordinates> _extractionCoords = new();
        [SerializeField] private bool _requireAllLivingSquad = true;

        // Paramètres Survie
        [SerializeField] private int _targetRounds = 3;
        [SerializeField] private int _startRound = 1;

        // Paramètres Escorte VIP
        [SerializeField] private TacticalUnit _vipUnit;
        [SerializeField] private string _vipUnitName = "VIP";
        [SerializeField] private List<HexCoordinates> _vipDestinationCoords = new();

        // Paramètres Piratage
        [SerializeField] private TacticalInteractable _targetInteractable;
        [SerializeField] private string _targetInteractableId = "";
        [SerializeField] private int _terminalsRequired = 1;
        [SerializeField] private int _terminalsHacked = 0;

        public string Id { get => _id; set => _id = value; }
        public string Title { get => _title; set => _title = value; }
        public string Description { get => _description; set => _description = value; }
        public CombatObjectiveType Type { get => _type; set => _type = value; }
        public bool IsOptional { get => _isOptional; set => _isOptional = value; }
        public CombatObjectiveStatus Status { get => _status; set => _status = value; }
        public string FailureReason { get => _failureReason; set => _failureReason = value; }

        public List<HexCoordinates> ExtractionCoords => _extractionCoords;
        public bool RequireAllLivingSquad { get => _requireAllLivingSquad; set => _requireAllLivingSquad = value; }

        public int TargetRounds { get => _targetRounds; set => _targetRounds = Mathf.Max(1, value); }
        public int StartRound { get => _startRound; set => _startRound = value; }

        public TacticalUnit VipUnit { get => _vipUnit; set => _vipUnit = value; }
        public string VipUnitName { get => _vipUnitName; set => _vipUnitName = value ?? ""; }
        public List<HexCoordinates> VipDestinationCoords => _vipDestinationCoords;

        public TacticalInteractable TargetInteractable { get => _targetInteractable; set => _targetInteractable = value; }
        public string TargetInteractableId { get => _targetInteractableId; set => _targetInteractableId = value ?? ""; }
        public int TerminalsRequired { get => _terminalsRequired; set => _terminalsRequired = Mathf.Max(1, value); }
        public int TerminalsHacked { get => _terminalsHacked; set => _terminalsHacked = Mathf.Max(0, value); }

        public bool IsCompleted => _status == CombatObjectiveStatus.Completed;
        public bool IsFailed => _status == CombatObjectiveStatus.Failed;
        public bool IsInProgress => _status == CombatObjectiveStatus.InProgress;

        public void ResetState()
        {
            _status = CombatObjectiveStatus.InProgress;
            _failureReason = "";
            _terminalsHacked = 0;
        }

        public void MarkCompleted()
        {
            _status = CombatObjectiveStatus.Completed;
        }

        public void MarkFailed(string reason = null)
        {
            _status = CombatObjectiveStatus.Failed;
            if (!string.IsNullOrEmpty(reason))
            {
                _failureReason = reason;
            }
        }

        public void RegisterTerminalHacked()
        {
            _terminalsHacked++;
            if (_terminalsHacked >= _terminalsRequired)
            {
                _status = CombatObjectiveStatus.Completed;
            }
        }

        public string GetTypeIcon()
        {
            return _type switch
            {
                CombatObjectiveType.Annihilation => "⚔️",
                CombatObjectiveType.Extraction => "🚀",
                CombatObjectiveType.SurviveRounds => "⏳",
                CombatObjectiveType.EscortVIP => "🛡️",
                CombatObjectiveType.HackTerminal => "💻",
                _ => "🎯"
            };
        }

        public string GetProgressString(TurnManager turnManager)
        {
            if (_status == CombatObjectiveStatus.Completed) return "✔ Complété";
            if (_status == CombatObjectiveStatus.Failed) return "❌ Échoué";

            switch (_type)
            {
                case CombatObjectiveType.Annihilation:
                    int enemyCount = 0;
                    if (turnManager != null)
                    {
                        var units = turnManager.TurnOrder;
                        for (int i = 0; i < units.Count; i++)
                        {
                            var u = units[i];
                            if (u != null && u.Stats != null && u.Stats.IsAlive && !u.IsPlayerControlled)
                                enemyCount++;
                        }
                    }
                    return $"{enemyCount} ennemi(s) restant(s)";

                case CombatObjectiveType.Extraction:
                    int inZone = 0;
                    int livingPlayers = 0;
                    if (turnManager != null)
                    {
                        var units = turnManager.TurnOrder;
                        for (int i = 0; i < units.Count; i++)
                        {
                            var u = units[i];
                            if (u != null && u.Stats != null && u.Stats.IsAlive && u.IsPlayerControlled)
                            {
                                livingPlayers++;
                                if (_extractionCoords != null && _extractionCoords.Contains(u.CurrentCoords))
                                    inZone++;
                            }
                        }
                    }
                    return $"En zone : {inZone}/{livingPlayers}";

                case CombatObjectiveType.SurviveRounds:
                    int curRound = turnManager != null ? turnManager.CurrentRound : 1;
                    return $"Round {Mathf.Min(curRound, _targetRounds)} / {_targetRounds}";

                case CombatObjectiveType.EscortVIP:
                    var vip = ResolveVipUnit(turnManager);
                    if (vip != null && vip.Stats != null)
                    {
                        if (!vip.Stats.IsAlive) return "💀 Hors de combat";
                        string destStr = "";
                        if (_vipDestinationCoords != null && _vipDestinationCoords.Count > 0)
                        {
                            int dist = int.MaxValue;
                            for (int i = 0; i < _vipDestinationCoords.Count; i++)
                                dist = Mathf.Min(dist, vip.CurrentCoords.DistanceTo(_vipDestinationCoords[i]));
                            destStr = $" (Dist: {dist} hex)";
                        }
                        return $"❤️ {vip.Stats.CurrentHealth}/{vip.Stats.MaxHealth} PV{destStr}";
                    }
                    return "En vie";

                case CombatObjectiveType.HackTerminal:
                    return $"Piraté : {_terminalsHacked}/{_terminalsRequired}";

                default:
                    return "En cours";
            }
        }

        public void Evaluate(TurnManager turnManager, int playerUnitsAlive, int enemyUnitsAlive)
        {
            if (_status == CombatObjectiveStatus.Failed)
                return;

            switch (_type)
            {
                case CombatObjectiveType.Annihilation:
                    if (_status == CombatObjectiveStatus.Completed) return;
                    if (enemyUnitsAlive == 0 && playerUnitsAlive > 0)
                    {
                        _status = CombatObjectiveStatus.Completed;
                    }
                    else if (playerUnitsAlive == 0)
                    {
                        _status = CombatObjectiveStatus.Failed;
                        _failureReason = "Escouade anéantie.";
                    }
                    break;

                case CombatObjectiveType.Extraction:
                    if (playerUnitsAlive == 0)
                    {
                        _status = CombatObjectiveStatus.Failed;
                        _failureReason = "Escouade neutralisée avant l'extraction.";
                        return;
                    }

                    if (_extractionCoords != null && _extractionCoords.Count > 0 && turnManager != null)
                    {
                        int inZone = 0;
                        var units = turnManager.TurnOrder;
                        for (int i = 0; i < units.Count; i++)
                        {
                            var u = units[i];
                            if (u != null && u.Stats != null && u.Stats.IsAlive && u.IsPlayerControlled)
                            {
                                if (_extractionCoords.Contains(u.CurrentCoords))
                                {
                                    inZone++;
                                }
                            }
                        }

                        if (_requireAllLivingSquad)
                        {
                            _status = (inZone >= playerUnitsAlive && playerUnitsAlive > 0)
                                ? CombatObjectiveStatus.Completed
                                : CombatObjectiveStatus.InProgress;
                        }
                        else
                        {
                            _status = (inZone > 0)
                                ? CombatObjectiveStatus.Completed
                                : CombatObjectiveStatus.InProgress;
                        }
                    }
                    break;

                case CombatObjectiveType.SurviveRounds:
                    if (_status == CombatObjectiveStatus.Completed) return;
                    if (playerUnitsAlive == 0)
                    {
                        _status = CombatObjectiveStatus.Failed;
                        _failureReason = "Escouade neutralisée avant d'avoir tenu les rounds requis.";
                        return;
                    }

                    if (turnManager != null && turnManager.CurrentRound >= _targetRounds)
                    {
                        _status = CombatObjectiveStatus.Completed;
                    }
                    break;

                case CombatObjectiveType.EscortVIP:
                    var vip = ResolveVipUnit(turnManager);

                    if (vip != null && vip.Stats != null)
                    {
                        if (!vip.Stats.IsAlive)
                        {
                            _status = CombatObjectiveStatus.Failed;
                            _failureReason = $"Échec critique : Le VIP ({vip.Stats.Name}) a succombé !";
                            return;
                        }

                        if (_vipDestinationCoords != null && _vipDestinationCoords.Count > 0)
                        {
                            _status = _vipDestinationCoords.Contains(vip.CurrentCoords)
                                ? CombatObjectiveStatus.Completed
                                : CombatObjectiveStatus.InProgress;
                        }
                        else if (enemyUnitsAlive == 0)
                        {
                            _status = CombatObjectiveStatus.Completed;
                        }
                    }
                    else if (playerUnitsAlive == 0)
                    {
                        _status = CombatObjectiveStatus.Failed;
                        _failureReason = "Escouade anéantie.";
                    }
                    break;

                case CombatObjectiveType.HackTerminal:
                    if (_status == CombatObjectiveStatus.Completed) return;
                    if (playerUnitsAlive == 0)
                    {
                        _status = CombatObjectiveStatus.Failed;
                        _failureReason = "Escouade neutralisée avant d'avoir piraté le terminal.";
                        return;
                    }

                    if (_targetInteractable != null && _targetInteractable.HasInteracted)
                    {
                        _terminalsHacked = Math.Max(_terminalsHacked, 1);
                    }

                    if (_terminalsHacked >= _terminalsRequired)
                    {
                        _status = CombatObjectiveStatus.Completed;
                    }
                    break;
            }
        }

        private TacticalUnit ResolveVipUnit(TurnManager turnManager)
        {
            if (_vipUnit != null) return _vipUnit;
            if (turnManager == null || string.IsNullOrEmpty(_vipUnitName)) return null;

            var list = turnManager.TurnOrder;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u != null && u.Stats != null &&
                    (string.Equals(u.Stats.Name, _vipUnitName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(u.gameObject.name, _vipUnitName, StringComparison.OrdinalIgnoreCase)))
                {
                    _vipUnit = u;
                    return _vipUnit;
                }
            }
            return null;
        }

        // =========================================================================
        // FACTORY HELPERS
        // =========================================================================

        public static CombatObjective CreateAnnihilation(string title = "Élimination Totale", string desc = "Neutraliser toutes les forces hostiles.", bool isOptional = false)
        {
            return new CombatObjective
            {
                Title = title,
                Description = desc,
                Type = CombatObjectiveType.Annihilation,
                IsOptional = isOptional
            };
        }

        public static CombatObjective CreateExtraction(IEnumerable<HexCoordinates> coords, bool requireAllLivingSquad = true, string title = "Zone d'Extraction", string desc = "Évacuer l'escouade vers le point d'extraction.", bool isOptional = false)
        {
            var obj = new CombatObjective
            {
                Title = title,
                Description = desc,
                Type = CombatObjectiveType.Extraction,
                RequireAllLivingSquad = requireAllLivingSquad,
                IsOptional = isOptional
            };
            if (coords != null) obj.ExtractionCoords.AddRange(coords);
            return obj;
        }

        public static CombatObjective CreateSurvive(int targetRounds, string title = "Tenir la Position", string desc = null, bool isOptional = false)
        {
            return new CombatObjective
            {
                Title = title,
                Description = desc ?? $"Survivez pendant au moins {targetRounds} rounds face aux assauts.",
                Type = CombatObjectiveType.SurviveRounds,
                TargetRounds = targetRounds,
                IsOptional = isOptional
            };
        }

        public static CombatObjective CreateEscort(TacticalUnit vip, IEnumerable<HexCoordinates> destinationCoords = null, string title = "Escorte VIP", string desc = null, bool isOptional = false)
        {
            var obj = new CombatObjective
            {
                Title = title,
                Description = desc ?? $"Protéger le VIP ({vip?.Stats?.Name ?? "Cible"}). Sa mort entraîne l'échec de la mission.",
                Type = CombatObjectiveType.EscortVIP,
                VipUnit = vip,
                VipUnitName = vip != null ? (vip.Stats != null ? vip.Stats.Name : vip.name) : "VIP",
                IsOptional = isOptional
            };
            if (destinationCoords != null) obj.VipDestinationCoords.AddRange(destinationCoords);
            return obj;
        }

        public static CombatObjective CreateHackTerminal(TacticalInteractable terminal, string title = "Piratage de la Console", string desc = null, bool isOptional = false)
        {
            return new CombatObjective
            {
                Title = title,
                Description = desc ?? $"Accéder à la console {terminal?.ObjectName ?? "Terminal"} et pirater le système.",
                Type = CombatObjectiveType.HackTerminal,
                TargetInteractable = terminal,
                TargetInteractableId = terminal != null ? terminal.ObjectName : "",
                TerminalsRequired = 1,
                IsOptional = isOptional
            };
        }

        public static CombatObjective CreateHackTerminalById(string terminalId, int count = 1, string title = "Piratage Terminal", string desc = null, bool isOptional = false)
        {
            return new CombatObjective
            {
                Title = title,
                Description = desc ?? $"Pirater le terminal {terminalId}.",
                Type = CombatObjectiveType.HackTerminal,
                TargetInteractableId = terminalId ?? "",
                TerminalsRequired = count,
                IsOptional = isOptional
            };
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    /// <summary>
    /// Interface utilisateur tête haute (HUD) pour le combat tactique.
    /// Affiche les Points d'Action (PA), les jauges d'encaissement,
    /// le sélecteur anatomique de tir ciblé et les logs de combat.
    /// </summary>
    public class CombatHUD : MonoBehaviour
    {
        [Header("Contrôleurs")]
        [SerializeField] private TurnManager _turnManager;

        private readonly List<string> _combatLogs = new();
        private Vector2 _logScroll;
        private BodyPart _selectedBodyPart = BodyPart.Torse;
        private bool _cancelPenaltyWithAP = false;

        public event Action<BodyPart, bool> OnAttackRequested;
        public event Action OnEndTurnRequested;
        public event Action OnEmergencyBreathRequested;

        public void AddCombatLog(string message)
        {
            _combatLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (_combatLogs.Count > 40) _combatLogs.RemoveAt(0);
        }

        private void OnGUI()
        {
            var unit = _turnManager != null ? _turnManager.ActiveUnit : null;
            if (unit == null) return;

            DrawTopBar(unit);
            DrawTargetedShotPanel(unit);
            DrawCombatLogWindow();
        }

        private void DrawTopBar(TacticalUnit unit)
        {
            GUILayout.BeginArea(new Rect(20, 20, 420, 140), GUI.skin.box);
            GUILayout.Label($"<b>{unit.Stats.Name}</b> — Tour de 10s (Round {_turnManager.CurrentRound})", GUI.skin.label);

            // Jauge de Points d'Action
            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ Points d'Action (PA) : <b>{unit.Stats.CurrentActionPoints} / {unit.Stats.MaxActionPoints}</b>");

            // Jauge de Vitalité & Encaissement
            GUI.color = Color.green;
            GUILayout.Label($"❤️ Vitalité : <b>{unit.Stats.CurrentHealth} / {unit.Stats.MaxHealth} PV</b> (Encaissement: {unit.Stats.EncaissementThreshold})");

            // Essoufflement
            GUI.color = (unit.Stats.Essoufflement > 0) ? Color.yellow : Color.white;
            GUILayout.Label($"🫁 Essoufflement : {unit.Stats.Essoufflement} / {unit.Stats.Attributes.Constitution}");

            // Statuts actifs
            if (unit.Stats.ActiveStatus != StatusEffect.None)
            {
                GUI.color = Color.red;
                GUILayout.Label($"⚠️ Statuts : {unit.Stats.ActiveStatus}");
            }

            GUI.color = Color.white;
            GUILayout.EndArea();
        }

        private void DrawTargetedShotPanel(TacticalUnit unit)
        {
            GUILayout.BeginArea(new Rect(20, 170, 320, 340), "Ciblage Anatomique (Livre VI)", GUI.skin.window);

            GUILayout.Label("Sélectionnez la partie du corps visée :");

            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
            {
                var info = BodyPartInfo.GetInfo(part);
                string label = $"{info.DisplayName} ({info.DifficultyModifier} malus)";
                if (GUILayout.Toggle(_selectedBodyPart == part, label))
                {
                    _selectedBodyPart = part;
                }
            }

            GUILayout.Space(6);
            _cancelPenaltyWithAP = GUILayout.Toggle(_cancelPenaltyWithAP, "Dépenser +1 PA pour annuler le malus de visée");

            GUILayout.Space(8);
            GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
            if (GUILayout.Button("🎯 Déclencher l'Attaque Ciblée (2 PA)"))
            {
                OnAttackRequested?.Invoke(_selectedBodyPart, _cancelPenaltyWithAP);
            }

            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
            if (GUILayout.Button("🫁 Souffle d'Urgence (+2 PA, +1 Essoufflement)"))
            {
                if (unit.Stats.TakeEmergencyBreath(2))
                {
                    AddCombatLog($"{unit.Stats.Name} puise dans son souffle d'urgence (+2 PA) !");
                    OnEmergencyBreathRequested?.Invoke();
                }
            }

            GUI.backgroundColor = Color.gray;
            if (GUILayout.Button("Fin du Tour"))
            {
                _turnManager.EndCurrentTurn();
                OnEndTurnRequested?.Invoke();
            }

            GUI.backgroundColor = Color.white;
            GUILayout.EndArea();
        }

        private void DrawCombatLogWindow()
        {
            float width = 450;
            float height = 180;
            GUILayout.BeginArea(new Rect(Screen.width - width - 20, Screen.height - height - 20, width, height), "Chroniques du Combat", GUI.skin.window);
            _logScroll = GUILayout.BeginScrollView(_logScroll);

            for (int i = _combatLogs.Count - 1; i >= 0; i--)
            {
                GUILayout.Label(_combatLogs[i]);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}

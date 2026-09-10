using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Tactics;
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
            if (_turnManager != null && _turnManager.IsCombatOver)
            {
                DrawCombatOutcomeBanner();
                DrawCombatLogWindow();
                return;
            }

            var unit = _turnManager != null ? _turnManager.ActiveUnit : null;
            if (unit == null) return;

            DrawTopBar(unit);
            DrawTargetedShotPanel(unit);
            DrawCombatLogWindow();
        }

        private void DrawCombatOutcomeBanner()
        {
            bool isVictory = (_turnManager.CurrentOutcome == CombatOutcome.Victory);

            float width = 480;
            float height = 220;
            Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.4f, width, height);

            GUI.backgroundColor = isVictory ? new Color(0.1f, 0.35f, 0.15f, 0.95f) : new Color(0.45f, 0.1f, 0.1f, 0.95f);
            GUILayout.BeginArea(rect, GUI.skin.window);

            string title = isVictory ? "🏆 VICTOIRE TACTIQUE" : "💀 DÉFAITE CRITIQUE";
            string desc = isVictory 
                ? "Tous les opposants ont été neutralisés ou mis hors de combat." 
                : "L'escouade opérationnelle a été décimée ou plongée dans l'inconscience.";

            GUI.color = isVictory ? Color.green : Color.red;
            GUILayout.Label($"<size=20><b>{title}</b></size>", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;

            GUILayout.Space(8);
            GUILayout.Label(desc, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true });

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();

            var arena = FindAnyObjectByType<CombatDevArena>();

            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.9f);
            if (GUILayout.Button("⏪ Rembobiner (Livre V)", GUILayout.Height(38)))
            {
                arena?.RewindLastSnapshot();
            }

            GUI.backgroundColor = isVictory ? new Color(0.2f, 0.8f, 0.3f) : new Color(0.9f, 0.3f, 0.2f);
            if (GUILayout.Button("🔄 Recommencer l'Arène", GUILayout.Height(38)))
            {
                arena?.ResetArena();
            }

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
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

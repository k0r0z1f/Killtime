using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;
using Killtime.Core.Chrono;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Multi;
using Killtime.Story;

// Partial move-only de CombatDevToolbar (facade : CombatDevToolbar.cs) — 0 logique modifiée.
namespace Killtime.UI
{
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        private void DrawChronomancyTab()
        {
            GUILayout.Label("<b>⏳ Le Fleuve du Temps (Livre V) — Sauvegardes & Rembobinage :</b>");
            GUILayout.Space(4);

            GUI.backgroundColor = new Color(0.2f, 0.85f, 1.0f);
            if (GUILayout.Button("🌊 Ouvrir le Fleuve du Temps 3D (F7)", GUILayout.Height(36)))
            {
                RiverOfTimeDevWindow.Open();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(6);

            GUILayout.Label("<b>Ligne causale (0/A/B/C/D) :</b>");
            GUILayout.BeginHorizontal();
            var branchIds = new[] { TimelineId.Timeline0_Prime, TimelineId.TimelineA_Alpha, TimelineId.TimelineB_Beta, TimelineId.TimelineC_Gamma, TimelineId.TimelineD_Delta };
            var branchLabels = new[] { "0 Prime", "A Alpha", "B Beta", "C Gamma", "D Delta" };
            for (int b = 0; b < branchIds.Length; b++)
            {
                bool isActive = _arena != null && _arena.ActiveChronoBranch.Equals(branchIds[b]);
                if (isActive) GUI.backgroundColor = new Color(0.2f, 0.85f, 1.0f);
                if (GUILayout.Button(branchLabels[b], GUILayout.Height(26)))
                {
                    _arena?.SwitchChronoBranch(branchIds[b]);
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            int snapshotCount = _arena != null && _arena.Timeline != null ? _arena.Timeline.GetFullChronology().Count : 0;
            GUILayout.Label($"Nombre de Snapshots temporels enregistrés : <b>{snapshotCount}</b>");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("💾 Créer un Snapshot Manuel", GUILayout.Height(32)))
            {
                _arena?.RecordChronoSnapshot("Snapshot manuel développeur");
            }

            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.9f);
            if (GUILayout.Button("⏪ REMBOBINER LA DERNIÈRE ACTION", GUILayout.Height(32)))
            {
                _arena?.RewindLastSnapshot();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Historique chronologique (⏪ = rembobiner jusqu'ici) :");
            if (_arena != null && _arena.Timeline != null)
            {
                var history = _arena.Timeline.GetFullChronology();
                for (int i = history.Count - 1; i >= Mathf.Max(0, history.Count - 6); i--)
                {
                    var s = history[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"• [Round {s.RoundNumber} / {s.SecondInRound:0.0}s] {s.ActionDescription}");
                    if (GUILayout.Button("⏪", GUILayout.Width(36)))
                    {
                        _arena?.RewindToSnapshot(i);
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void DrawLogsTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Historique des Actions et Mathématiques de Combat :</b>");
            bool newLock = GUILayout.Toggle(_scrollLock, "🔒 Scroll Lock", GUILayout.Width(110));
            if (newLock != _scrollLock)
            {
                _scrollLock = newLock;
                if (!_scrollLock) _logScrollToBottomPending = true;
                else _logScrollToBottomPending = false;
            }
            if (GUILayout.Button("Effacer", GUILayout.Width(70)))
            {
                _logs.Clear();
                _logScroll.y = 0f;
                _logScrollToBottomPending = false;
            }
            GUILayout.EndHorizontal();

            if (_scrollLock)
            {
                // Verrou : fige la position lue par l'utilisateur.
                _logScrollToBottomPending = false;
            }
            else if (_logScrollToBottomPending)
            {
                _logScroll.y = float.MaxValue;
                _logScrollToBottomPending = false;
            }

            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.Height(320));
            // Ordre chronologique (ancien en haut, récent en bas) comme le feed
            // tactique : l'auto-scroll va en bas vers le plus récent.
            for (int i = 0; i < _logs.Count; i++)
            {
                GUILayout.Label(_logs[i]);
            }
            GUILayout.EndScrollView();
        }
    }
}

using UnityEngine;
using Killtime.Tactics;
using Killtime.Core.Combat;

namespace Killtime.UI
{
    // RD-198 : section scénarios terrain 1-clic + carnet d'écarts. Appelée depuis
    // DrawCheatsAndToolsTab (1 ligne), dev-only, 0 règle modifiée.
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        private int _scenarioPostureIndex;

        private void DrawScenariosSection()
        {
            if (_arena == null) return;

            GUILayout.Space(8);
            GUILayout.Label("<b>🎬 Scénarios terrain 1-clic (RD-198, Livres VI §24-26) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            var actor = _arena.PlayerUnit;
            var foe = _arena.CurrentTarget;
            if (actor != null && actor.Stats != null)
                GUILayout.Label($"Acteur : <b>{actor.Stats.Name}</b> ({actor.Stats.CurrentActionPoints} PA)");
            if (foe != null && foe.Stats != null)
                GUILayout.Label($"Cible : <b>{foe.Stats.Name}</b> — sélectionnez-la au préalable (clic).");
            else
                GUILayout.Label("<i>Aucune cible : sélectionnez un traverseur/voisin avant le setup.</i>");

            if (GUILayout.Button("🎯 SCÉNARIO GUET (2 PA, traverseur à ~4 cases)", GUILayout.Height(28)))
                _arena.SetupScenarioGuet();
            GUILayout.Label("<i>Ensuite : déplacez la cible dans le rayon (tour adverse/FullAuto). Attendu : 1 tir/round, purge au tour.</i>");

            string[] postures = { "Frappe", "Poursuite", "Blocage", "Balayage" };
            GUILayout.BeginHorizontal();
            GUILayout.Label("Posture :", GUILayout.Width(70));
            for (int i = 0; i < postures.Length; i++)
            {
                if (i == _scenarioPostureIndex) GUI.backgroundColor = new Color(1f, 0.75f, 0.2f);
                if (GUILayout.Button(postures[i], GUILayout.Height(24))) _scenarioPostureIndex = i;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button($"🤼 SCÉNARIO OPPORTUNITÉ ({CombatDevArena.ScenarioPostureLabel(_scenarioPostureIndex)}, contact)", GUILayout.Height(28)))
                _arena.SetupScenarioOpportunite(CombatDevArena.ScenarioPostureFromIndex(_scenarioPostureIndex));
            GUILayout.Label("<i>Ensuite : faites quitter le contact au voisin. Attendu : réaction 1/round (Décrochage 1 PA = sans réaction).</i>");

            if (GUILayout.Button("⚡ SCÉNARIO CHARGE (cible à 3+ cases)", GUILayout.Height(28)))
                _arena.SetupScenarioCharge();
            GUILayout.Label("<i>Ensuite : ⚡ Charge au radial. Attendu : +2 dégâts d'impact. Surplomb +1 palier si relief.</i>");

            GUILayout.Space(4);
            GUILayout.Label("<b>⭕ Checklist radiale (RD-184, rien à coder) :</b>");
            GUILayout.Label($"<i>{CombatDevArena.RadialChecklistText()}</i>");

            GUILayout.Space(4);
            GUILayout.Label("<b>🎬 Démo 60s (RD-186) :</b>");
            GUILayout.Label($"<i>{CombatDevArena.DemoChecklistText()}</i>");
            GUILayout.Label("<i>Lancez-la au panneau DÉMO 60s en bas à gauche (Killzone_Alpha3 uniquement).</i>");

            GUILayout.Space(4);
            if (GUILayout.Button("📋 COPIER CARNET D'ÉCARTS + LOGS (presse-papiers)", GUILayout.Height(30)))
            {
                string report = CombatDevArena.BuildGapReport(_logs);
                try { GUIUtility.systemCopyBuffer = report; } catch { /* headless */ }
                _arena.Log("📋 <b>Carnet d'écarts copié</b> : cochez [OK]/[ÉCART] en jeu, reportez les écarts en fix.");
            }

            GUILayout.EndVertical();
        }
    }
}

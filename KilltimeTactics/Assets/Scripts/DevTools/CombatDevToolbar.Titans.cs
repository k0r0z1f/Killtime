using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.UI
{
    // RD-196 : section Titans + boss 1-clic de la toolbar MJ. Appelée depuis
    // DrawCheatsAndToolsTab (1 ligne), dev-only, 0 règle modifiée.
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        private int _titanBossIndex;

        private void DrawTitansSection()
        {
            if (_arena == null) return;

            GUILayout.Space(8);
            GUILayout.Label("<b>🦣 Titans multi-cases + Boss (RD-196, Livres V §22.3 / XII §49-50) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            HexCoordinates spawnAt = _arena.CurrentTarget != null
                ? _arena.CurrentTarget.CurrentCoords
                : (_arena.PlayerUnit != null ? _arena.PlayerUnit.CurrentCoords : new HexCoordinates(2, 0));
            GUILayout.Label($"<i>Spawn au point ciblé ({spawnAt.Q},{spawnAt.R}) — empreinte + périmètre vérifiés au log.</i>");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🦣 + Rosette7 (7 hex)", GUILayout.Height(26)))
                _arena.SpawnTitanDummy(TitanFootprintType.Rosette7, spawnAt, false);
            if (GUILayout.Button("🦣 + Colossus19 (19 hex)", GUILayout.Height(26)))
                _arena.SpawnTitanDummy(TitanFootprintType.Colossus19, spawnAt, false);
            GUILayout.EndHorizontal();

            string[] bossNames = { "👑 Bête d'Hybris", "👑 Minulican", "👑 Disciple de Terre" };
            GUILayout.BeginHorizontal();
            for (int i = 0; i < bossNames.Length; i++)
            {
                if (i == _titanBossIndex) GUI.backgroundColor = new Color(1f, 0.75f, 0.2f);
                if (GUILayout.Button(bossNames[i], GUILayout.Height(26))) _titanBossIndex = i;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"⚔️ Spawner {bossNames[_titanBossIndex]} ENNEMI", GUILayout.Height(28)))
                _arena.SpawnTitanBoss(_titanBossIndex, spawnAt, false);
            if (GUILayout.Button("📖 Recharger bossAI JSON", GUILayout.Height(28)))
                _arena.ReloadTitanBossAI();
            GUILayout.EndHorizontal();
            GUILayout.Label($"<i>Source : {BestiaryCatalog.LoadedFrom} · {BestiaryCatalog.Count} entrées (touche B = Bestiaire, bossAI éditable).</i>");

            var activeUnit = _turnManager != null ? _turnManager.ActiveUnit : _arena.PlayerUnit;
            var foe = _arena.CurrentTarget;
            if (activeUnit != null && activeUnit.Stats != null)
                GUILayout.Label($"Acteur : <b>{activeUnit.Stats.Name}</b> ({activeUnit.Stats.CurrentActionPoints} PA) — {_arena.VerifyTitanFootprint(activeUnit)}");
            else
                GUILayout.Label("<i>Aucune unité active.</i>");

            bool actorTitan = activeUnit != null && TitanFootprint.IsTitan(activeUnit.FootprintType);
            bool foeAlive = foe != null && foe.Stats != null && foe.Stats.IsAlive;
            if (!actorTitan)
                GUILayout.Label("<i>Coups 1-clic : sélectionnez un Titan/boss comme unité active.</i>");
            else if (!foeAlive)
                GUILayout.Label("<i>Coups 1-clic : aucune cible active vivante.</i>");
            else
            {
                int dist = TitanFootprint.MinDistanceBetweenUnits(activeUnit.CurrentCoords, activeUnit.FootprintType, foe.CurrentCoords, foe.FootprintType);
                GUILayout.Label($"Cible : <b>{foe.Stats.Name}</b> en ({foe.CurrentCoords.Q},{foe.CurrentCoords.R}) — distance périmètre {dist}.");
                GUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(1f, 0.75f, 0.2f);
                if (GUILayout.Button("✊ SAISIE 1-clic (2 PA)", GUILayout.Height(30)))
                    _arena.ExecuteTitanGrab1Click(activeUnit, foe);
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.2f);
                if (GUILayout.Button("💥 PIÉTINEMENT 1-clic (3 PA)", GUILayout.Height(30)))
                    _arena.ExecuteTitanStomp1Click(activeUnit, foe.CurrentCoords);
                GUI.backgroundColor = new Color(1f, 0.5f, 0.1f);
                if (GUILayout.Button("🔥 SOUFFLE 1-clic (3 PA)", GUILayout.Height(30)))
                    _arena.ExecuteTitanBreath1Click(activeUnit, foe.CurrentCoords);
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.Label("<i>Grab : contact périmètre + gabarit inférieur. Stomp : portée 2. Souffle : portée 8. Réactions boss tracées au log.</i>");
            }

            GUILayout.EndVertical();
        }
    }
}

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
        private void DrawCheatsAndToolsTab()
        {
            var ai = (_arena != null ? _arena.AIController : null) ?? FindAnyObjectByType<TacticalAIController>();
            if (ai != null)
            {
                GUILayout.Label("<b>🤖 Automatisation Tactique (IA) :</b>");
                GUILayout.BeginVertical(GUI.skin.box);

                bool inRoom = VTTRoomManager.Instance != null && VTTRoomManager.Instance.InRoom;
                bool isGM = inRoom && VTTRoomManager.Instance.IsGM;
                bool isPlayer = inRoom && !isGM;

                if (isPlayer)
                {
                    GUI.color = new Color(1f, 0.75f, 0.2f);
                    GUILayout.Label("🔒 <b>Paramètres d'IA sous contrôle du GM</b> (L'IA s'exécute côté GM)");
                    GUI.color = Color.white;
                }
                else if (isGM)
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = new Color(0.4f, 0.9f, 1.0f);
                    GUILayout.Label("👑 <b>Maître du Jeu (GM)</b> — Paramètres synchronisés aux joueurs");
                    GUI.color = Color.white;
                    if (GUILayout.Button("🔄 Synchroniser", GUILayout.Width(100)))
                    {
                        VTTTableSync.Instance?.BroadcastRoomSettings();
                    }
                    GUILayout.EndHorizontal();
                }

                // Ne jamais réactiver un contenu docké (base : GUI.enabled=false).
                bool prevEnabledAI = GUI.enabled;
                GUI.enabled = !isPlayer && prevEnabledAI;

                bool newAiEnabled = GUILayout.Toggle(ai.IsAIEnabled, "Activer le Contrôleur d'IA");
                if (newAiEnabled != ai.IsAIEnabled)
                {
                    ai.IsAIEnabled = newAiEnabled;
                    if (DevUIPreferences.Current != null)
                    {
                        DevUIPreferences.Current.AiEnabled = newAiEnabled;
                        DevUIPreferences.MarkDirty();
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Mode IA :", GUILayout.Width(80));

                bool isNormal = (ai.Mode == CombatAIMode.Normal);
                bool isFullAuto = (ai.Mode == CombatAIMode.FullAuto);

                GUI.backgroundColor = isNormal ? new Color(0.2f, 0.6f, 1f) : Color.white;
                if (GUILayout.Button("Mode Normal (Ennemis IA)"))
                {
                    ai.Mode = CombatAIMode.Normal;
                    if (DevUIPreferences.Current != null)
                    {
                        DevUIPreferences.Current.AiMode = (int)CombatAIMode.Normal;
                        DevUIPreferences.MarkDirty();
                    }
                }

                GUI.backgroundColor = isFullAuto ? new Color(1f, 0.4f, 0.2f) : Color.white;
                if (GUILayout.Button("Mode Full Auto (100% IA)"))
                {
                    ai.Mode = CombatAIMode.FullAuto;
                    if (DevUIPreferences.Current != null)
                    {
                        DevUIPreferences.Current.AiMode = (int)CombatAIMode.FullAuto;
                        DevUIPreferences.MarkDirty();
                    }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Délai d'action : {ai.ActionDelay:0.00}s", GUILayout.Width(150));
                float curDelay = ai.ActionDelay;
                float newDelay = GUILayout.HorizontalSlider(curDelay, 0.05f, 6.0f);
                if (Mathf.Abs(newDelay - curDelay) > 0.01f)
                {
                    ai.ActionDelay = newDelay;
                    if (DevUIPreferences.Current != null)
                    {
                        DevUIPreferences.Current.AiActionDelay = newDelay;
                        DevUIPreferences.MarkDirty();
                    }
                }
                GUILayout.EndHorizontal();

                GUI.enabled = prevEnabledAI;

                GUILayout.EndVertical();
                GUILayout.Space(6);
            }

            GUILayout.Label("<b>⚡ Manipulation des Points d'Action & Vitalité :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⚡ Recharger Tous les PA")) _arena?.RefillAPAll();
            if (GUILayout.Button("❤️ Soin Complet (Tous)")) _arena?.HealAndCureAll();
            GUILayout.EndHorizontal();

            if (_arena != null)
            {
                _arena.InfiniteAP = GUILayout.Toggle(_arena.InfiniteAP, "♾️ PA Infinis (Cheat Développeur)");
                GUILayout.Label("🎯 Ligne de Mire Couvert (vert 0 / jaune -1 / orange -2 / rouge impossible) :", GUILayout.Height(20));
                string coverBtnLabel = _arena.ShowCoverLineOfSight
                    ? "👁️ Raycasts Cover : VISIBLES — Cliquer pour masquer"
                    : "👁️‍🗨️ Raycasts Cover : MASQUÉS — Cliquer pour afficher";
                GUI.backgroundColor = _arena.ShowCoverLineOfSight ? new Color(0.35f, 0.9f, 0.45f) : new Color(0.6f, 0.6f, 0.6f);
                if (GUILayout.Button(coverBtnLabel, GUILayout.Height(30)))
                {
                    _arena.ShowCoverLineOfSight = !_arena.ShowCoverLineOfSight;
                }
                GUI.backgroundColor = Color.white;
                _arena.EnterCombatOnMapLoad = GUILayout.Toggle(_arena.EnterCombatOnMapLoad, "⚔️ Combat auto au chargement de carte");
                _arena.AutoEndTurnOnEmptyPA = GUILayout.Toggle(_arena.AutoEndTurnOnEmptyPA, "⏭️ Tour suivant auto à 0 PA (allié)");
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>💀 Élimination Ciblée :</b>");
            GUI.backgroundColor = new Color(0.7f, 0.1f, 0.1f);
            if (GUILayout.Button("💀 KO Instantané de la Cible Active"))
            {
                _arena?.KillCurrentTarget();
            }
            GUI.backgroundColor = Color.white;

            DrawObjectivesSection();

            GUILayout.Space(8);
            GUILayout.Label("<b>🖥️ Système & Multitâche (Alt-Tab / Multi-Écran) :</b>");
            bool runBg = Application.runInBackground;
            string bgBtnLabel = runBg
                ? "🖥️ Multitâche : SANS PAUSE (Le jeu tourne en arrière-plan)"
                : "⏸️ Multitâche : PAUSE AUTO (Le jeu se fige hors focus)";
            GUI.backgroundColor = runBg ? new Color(0.35f, 0.9f, 0.45f) : new Color(0.9f, 0.4f, 0.35f);
            if (GUILayout.Button(bgBtnLabel, GUILayout.Height(30)))
            {
                Application.runInBackground = !runBg;
                if (DevUIPreferences.Current != null)
                {
                    DevUIPreferences.Current.RunInBackground = Application.runInBackground;
                    DevUIPreferences.MarkDirty();
                }
            }
            GUI.backgroundColor = new Color(0.2f, 0.5f, 0.8f);
            if (GUILayout.Button("🖥️ Changer d'Écran / Moniteur (Ctrl + Shift + D)", GUILayout.Height(28)))
            {
                var dm = FindAnyObjectByType<KilltimeDisplayManager>();
                if (dm != null) dm.CycleNextDisplay();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);
            GUILayout.Label("<b>📐 Presets de Layout d'Arène :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1. Anneau Duel (Ouvert)")) _arena?.ApplyArenaLayout(ArenaLayoutType.TheDuelRing);
            if (GUILayout.Button("2. Barricades Tactiques")) _arena?.ApplyArenaLayout(ArenaLayoutType.TacticalBarricades);
            if (GUILayout.Button("3. Chokepoint Étroit")) _arena?.ApplyArenaLayout(ArenaLayoutType.KillzoneChokepoint);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("<b>🎥 Caméra & Immersion :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("👁️ Toggle Free Look (Touche F)"))
            {
                _cinematicDirector?.ToggleFreeLook();
            }
            string resetBtnLabel = (_arena != null && _arena.HasLoadedMap && _arena.CurrentLoadedMap != null)
                ? $"🔄 Réinitialiser Carte ({_arena.CurrentLoadedMap.MapName})"
                : "🔄 Réinitialiser l'Arène";
            if (GUILayout.Button(resetBtnLabel))
            {
                _arena?.ResetArena();
            }
            GUILayout.EndHorizontal();
            TacticalUnitVisual.ShowOverheadHUD = GUILayout.Toggle(TacticalUnitVisual.ShowOverheadHUD, "🏷️ Boîtes d'info personnages (Nom + PV/PA — Touche N)");

            GUILayout.Space(8);
            GUILayout.Label("<b>⏳ Vitesse du Jeu (0.01× – 100×) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            float toolbarScale = TimeScaleDevWindow.CurrentTimeScale;
            GUILayout.BeginHorizontal();
            GUI.color = toolbarScale < 1f ? new Color(1f, 0.6f, 0.2f)
                : toolbarScale > 1f ? new Color(0.4f, 0.9f, 1f) : Color.white;
            GUILayout.Label($"Vitesse : <b>{TimeScaleDevWindow.FormatScale(toolbarScale)}</b>", GUILayout.Width(130));
            GUI.color = Color.white;
            if (GUILayout.Button("⏳ Ouvrir Vitesse (F10)", GUILayout.Height(26)))
            {
                TimeScaleDevWindow.Open();
            }
            if (GUILayout.Button("▶ 1×", GUILayout.Width(52), GUILayout.Height(26)))
            {
                TimeScaleDevWindow.ResetToNormal();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>Ralentit/accélère tout (anims, cinématiques, IA). Piloté par la fenêtre Vitesse du Jeu (F10), Shift+F10 = 1×.</i>");
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>💡 Éclairage & Atmosphère :</b>");

            string fullbrightLabel = _devFullbrightActive ? "💡 Lumière Globale : ACTIVÉE (Touche L)" : "💡 Lumière Globale : ÉTEINTE (Touche L)";
            GUI.backgroundColor = _devFullbrightActive ? new Color(1.0f, 0.85f, 0.35f) : Color.white;
            if (GUILayout.Button(fullbrightLabel, GUILayout.Height(32)))
            {
                ToggleDevFullbrightLight();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✨ Invoquer Pixie de Test", GUILayout.Height(30)))
            {
                Killtime.Tactics.Lighting.TacticalPixieLight.SpawnPixie();
            }
            if (GUILayout.Button("🧹 Retirer Pixies", GUILayout.Height(30)))
            {
                var pixies = FindObjectsByType<Killtime.Tactics.Lighting.TacticalPixieLight>();
                for (int i = 0; i < pixies.Length; i++)
                {
                    Destroy(pixies[i].gameObject);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("<b>👥 Personnages & Fiches :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🧙 Créateur Personnages (F1)", GUILayout.Height(32)))
            {
                CharacterDevWindow.Open();
            }
            if (GUILayout.Button("🗡️ Ancrage Armes / Grip (F5)", GUILayout.Height(32)))
            {
                WeaponGripEditorDevWindow.Open();
            }
            if (GUILayout.Button("📖 Bestiaire (F6)", GUILayout.Height(32)))
            {
                BestiaryDevWindow.Open();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>🗺️ Conception de Champ de Bataille :</b>");
            if (GUILayout.Button("🗺️ Ouvrir l'Éditeur de Carte (F2)", GUILayout.Height(32)))
            {
                MapEditorDevWindow.Open();
            }

            GUILayout.Space(4);
            GUILayout.Label("<b>⚖️ Moteur de Règles & Équilibrage :</b>");
            if (GUILayout.Button("⚖️ Table des Règles du Codex (F3)", GUILayout.Height(32)))
            {
                CoreRulesDevTableWindow.Open();
            }

            GUILayout.Space(4);
            GUILayout.Label("<b>🎒 Inventaire, Armurerie & Marché :</b>");
            if (GUILayout.Button("🎒🏪 Inventaire / Armurerie / Marché (F4 / I)", GUILayout.Height(32)))
            {
                InventoryDevWindow.Open();
            }
            if (GUILayout.Button("🛠️ Éditeur Armurerie JSON : tout le catalogue (F12)", GUILayout.Height(32)))
            {
                ArmoryDevWindow.Open();
            }

            GUILayout.Space(4);
            GUILayout.Label("<b>🌊 Chronomancie & Simulation Temporelle :</b>");
            if (GUILayout.Button("🌊 Ouvrir le Fleuve du Temps 3D (F7)", GUILayout.Height(32)))
            {
                RiverOfTimeDevWindow.Open();
            }

            GUILayout.Space(4);
            GUILayout.Label("<b>🗺️ Overworld & Campagne (réseau de secteurs) :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🗺️ Carte Monde Hybris (F8)", GUILayout.Height(32)))
            {
                Killtime.Story.HybrisWorldMapDevWindow.Open();
            }
            if (GUILayout.Button("🛠️ Éditeur World Map (F9)", GUILayout.Height(32)))
            {
                Killtime.Story.HybrisWorldMapEditorWindow.Open();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>🎬 Suite de Scène & Narration (Shift + F1..F4) :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🎬 Scènes & Choix (Shift+F1)", GUILayout.Height(32)))
            {
                ScenarioDevWindow.Open();
            }
            if (GUILayout.Button("🛠️ Éditeur Scènes (Shift+F2)", GUILayout.Height(32)))
            {
                ScenarioEditorDevWindow.Open();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🪐 Décor & Lumières (Shift+F3)", GUILayout.Height(32)))
            {
                SceneEnvironmentDevWindow.Open();
            }
            if (GUILayout.Button("🎬 Cinématiques (Shift+F4)", GUILayout.Height(32)))
            {
                CinematicEditorDevWindow.Open();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>🌐 Multijoueur (Table Virtuelle — Ctrl + F1..F2) :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🌐 Room VTT (Ctrl+F1)", GUILayout.Height(32)))
            {
                VTTRoomWindow.Open();
            }
            if (GUILayout.Button("📹 Salon Vidéo (Ctrl+F2)", GUILayout.Height(32)))
            {
                Killtime.Multi.Video.VTTVideoRoomWindow.Open();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(16);
        }

        private void DrawObjectivesSection()
        {
            if (_turnManager == null) return;

            GUILayout.Space(8);
            GUILayout.Label("<b>🎯 Objectifs Tactiques de Mission (RD-050) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            if (!_turnManager.HasCustomObjectives)
            {
                GUI.color = new Color(0.7f, 0.85f, 1f);
                GUILayout.Label("<i>Mode par défaut : Annihilation (Kill-All). Tous les ennemis abattus = Victoire.</i>");
                GUI.color = Color.white;
            }
            else
            {
                var objs = _turnManager.Objectives;
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    if (o == null) continue;
                    GUILayout.BeginHorizontal();
                    string statusIcon = o.IsCompleted ? "<color=#00FFAA>✔ COMPLÉTÉ</color>" : (o.IsFailed ? "<color=#FF4444>❌ ÉCHEC</color>" : "<color=#FFAA00>⏳ EN COURS</color>");
                    string optStr = o.IsOptional ? " [Optionnel]" : "";
                    GUILayout.Label($"{o.GetTypeIcon()} <b>{o.Title}</b>{optStr} : {statusIcon} ({o.GetProgressString(_turnManager)})");
                    if (GUILayout.Button("✔ Valider", GUILayout.Width(75)))
                    {
                        o.MarkCompleted();
                        _turnManager.CheckCombatOver();
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(4);
            _turnManager.AllowAnnihilationFallback = GUILayout.Toggle(_turnManager.AllowAnnihilationFallback, "⚔️ Victoire si tous les ennemis meurent (Fallback Pacification)");

            GUILayout.Space(4);
            GUILayout.Label("<b>Presets d'Objectifs Rapides :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⚔️ Annihilation"))
            {
                _turnManager.SetupAnnihilationPreset();
                _turnManager.CheckCombatOver();
            }
            if (GUILayout.Button("🚀 Extraction"))
            {
                var target = _arena != null && _arena.CurrentTarget != null ? _arena.CurrentTarget.CurrentCoords : new Killtime.Tactics.Grid.HexCoordinates(3, 0);
                var zone = new List<Killtime.Tactics.Grid.HexCoordinates> { target, target.GetNeighbor(0), target.GetNeighbor(1) };
                _turnManager.SetupExtractionPreset(zone, requireAllLiving: true);
                _turnManager.CheckCombatOver();
            }
            if (GUILayout.Button("⏳ Survie (+3 Rounds)"))
            {
                _turnManager.SetupSurvivePreset(3);
                _turnManager.CheckCombatOver();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🛡️ Escorte VIP"))
            {
                var vip = (_arena != null && _arena.CurrentTarget != null) ? _arena.CurrentTarget : _arena?.PlayerUnit;
                var dest = new List<Killtime.Tactics.Grid.HexCoordinates> { new Killtime.Tactics.Grid.HexCoordinates(4, 0) };
                _turnManager.SetupEscortPreset(vip, dest);
                _turnManager.CheckCombatOver();
            }
            if (GUILayout.Button("💻 Hacker Terminal"))
            {
                var term = FindAnyObjectByType<TacticalInteractable>();
                _turnManager.SetupHackTerminalPreset(term, "Console_Terminal");
                _turnManager.CheckCombatOver();
            }
            if (GUILayout.Button("🧹 Réinitialiser"))
            {
                _turnManager.ClearObjectives();
                _turnManager.CheckCombatOver();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
    }
}

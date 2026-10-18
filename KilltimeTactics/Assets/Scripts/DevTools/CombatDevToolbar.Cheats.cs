using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
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
        // RD-187 : index de l'unité ciblée par les cheats ciblés (sélecteur).
        private int _cheatUnitIndex = 0;

        // RD-197 : état du mode observe 3 tours (dev-only, UI seule, aucune règle changée).
        private bool _observeActive = false;
        private int _observeStartRound = 1;
        private float _observePrevDelay = 0.8f;

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

                DrawAiDoctrineSection(ai);

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

            DrawTargetedCheatsSection();
            DrawDotCareSection();
            DrawMoraleSection();
            DrawNamedZonesSection();
            DrawObjectivesSection();
            DrawVehiclesSection();
            DrawTitansSection();
            DrawDuoTechSection();

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

            DrawNightSection();

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

        // RD-197 : doctrines IA + observe 3 tours (dev-only, UI seule, 0 règle changée).
        // Prefs AiPersonality/AiRetreatRatio/AiDefensiveReserve déjà persistées :
        // ici seul le pilotage UI manquait. Couvre RD-183 (terrain doctrines).
        private void DrawAiDoctrineSection(TacticalAIController ai)
        {
            if (ai == null) return;
            GUILayout.Space(4);
            GUILayout.Label("<b>🧠 Doctrines IA (RD-197) :</b>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Doctrine :", GUILayout.Width(80));
            var doctrines = new[] { AIPersonality.Balanced, AIPersonality.Aggressive, AIPersonality.Tactician, AIPersonality.Survivor };
            var doctrineLabels = new[] { "Équilibré", "Agressif", "Tacticien", "Survivant" };
            for (int i = 0; i < doctrines.Length; i++)
            {
                bool active = ai.DefaultPersonality == doctrines[i];
                if (active) GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
                if (GUILayout.Button(doctrineLabels[i])) ai.DefaultPersonality = doctrines[i];
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Repli si PV < {(ai.RetreatHealthRatio * 100f):0}%", GUILayout.Width(150));
            float wantRetreat = GUILayout.HorizontalSlider(ai.RetreatHealthRatio, 0.05f, 0.6f);
            if (Mathf.Abs(wantRetreat - ai.RetreatHealthRatio) > 0.005f) ai.RetreatHealthRatio = wantRetreat;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Réserve défensive : {ai.DefensiveAPReserve} PA", GUILayout.Width(150));
            int wantReserve = Mathf.RoundToInt(GUILayout.HorizontalSlider(ai.DefensiveAPReserve, 0f, 2f));
            if (wantReserve != ai.DefensiveAPReserve) ai.DefensiveAPReserve = wantReserve;
            GUILayout.EndHorizontal();

            int curRound = _turnManager != null ? _turnManager.CurrentRound : 1;
            if (_observeActive && _turnManager != null && curRound >= _observeStartRound + 3)
            {
                ai.Mode = CombatAIMode.Normal;
                ai.ActionDelay = _observePrevDelay;
                _observeActive = false;
                _arena?.Log($"👁 <b>[DEV] Observe terminé</b> : round {curRound} atteint (+3 depuis {_observeStartRound}), retour Normal.");
            }

            GUILayout.BeginHorizontal();
            if (!_observeActive)
            {
                if (GUILayout.Button("👁 Observer 3 tours (FullAuto + 0.15s)"))
                {
                    _observeStartRound = _turnManager != null ? _turnManager.CurrentRound : 1;
                    _observePrevDelay = ai.ActionDelay;
                    ai.IsAIEnabled = true;
                    ai.Mode = CombatAIMode.FullAuto;
                    ai.ActionDelay = 0.15f;
                    _observeActive = true;
                    _arena?.Log($"👁 <b>[DEV] Observe 3 tours</b> : doctrine {ai.DefaultPersonality}, rounds {_observeStartRound}->{_observeStartRound + 3} (log tactique onglet 📜).");
                }
            }
            else
            {
                GUI.backgroundColor = new Color(1f, 0.4f, 0.2f);
                if (GUILayout.Button($"⏹ Stop observe (round {curRound}/{_observeStartRound + 3})", GUILayout.Height(26)))
                {
                    ai.Mode = CombatAIMode.Normal;
                    ai.ActionDelay = _observePrevDelay;
                    _observeActive = false;
                    _arena?.Log("👁 <b>[DEV] Observe interrompu</b> : retour Normal.");
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<i>Grenades tactiques : fumée=cut-vue, flash=Aveugle -4 tir avant assaut, gaz persistant (masque allié ignoré). Seuils IA utilitaire {Killtime.Tactics.AI.AIGrenadeScorer.UtilityPlayThreshold:0} / létal {Killtime.Tactics.AI.AIGrenadeScorer.LethalPlayThreshold:0} — log tactique = onglet 📜 Logs.</i>");
        }

        // RD-187 : socle cheats ciblés (dev-only). Sliders set-PV (dont seuil
        // moral 28 %) / set-PA/PE par unité, rig critique/raté (restauré après),
        // enrayement forcé, statuts on/off, purge chrono par branche + zones.
        private void DrawTargetedCheatsSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>🎯 Cheats Ciblés (RD-187, dev-only) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            var units = _arena != null ? _arena.AllDebugUnits() : null;
            if (units == null || units.Count == 0)
            {
                GUILayout.Label("<i>Aucune unité en arène.</i>");
                GUILayout.EndVertical();
                return;
            }
            _cheatUnitIndex = Mathf.Clamp(_cheatUnitIndex, 0, units.Count - 1);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < units.Count; i++)
            {
                var cand = units[i];
                if (cand == null || cand.Stats == null) continue;
                string nm = cand.Stats.Name ?? ("Unité " + i);
                if (nm.Length > 12) nm = nm.Substring(0, 12);
                if (i == _cheatUnitIndex) GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                if (GUILayout.Button(nm, GUILayout.Height(24))) _cheatUnitIndex = i;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            var unit = units[_cheatUnitIndex];
            if (unit == null || unit.Stats == null)
            {
                GUILayout.EndVertical();
                return;
            }
            var st = unit.Stats;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"❤️ PV : {st.CurrentHealth}/{st.MaxHealth}", GUILayout.Width(130));
            float wantHp = GUILayout.HorizontalSlider(st.CurrentHealth, 0f, Mathf.Max(1, st.MaxHealth));
            if (Mathf.Abs(wantHp - st.CurrentHealth) > 0.5f) _arena?.SetDebugHealth(unit, Mathf.RoundToInt(wantHp));
            if (GUILayout.Button("28%", GUILayout.Width(44)))
                _arena?.SetDebugHealth(unit, (int)Math.Ceiling(st.MaxHealth * 0.28f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"⚡ PA : {st.CurrentActionPoints}/{st.MaxActionPoints}", GUILayout.Width(130));
            float wantAp = GUILayout.HorizontalSlider(st.CurrentActionPoints, 0f, Mathf.Max(1, st.MaxActionPoints));
            if (Mathf.Abs(wantAp - st.CurrentActionPoints) > 0.5f) _arena?.SetDebugAP(unit, Mathf.RoundToInt(wantAp));
            GUILayout.EndHorizontal();

            int maxPe = Killtime.Tactics.Arena.ArenaChronoDebugService.GetMaxPEFor(st);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"😮‍💨 PE : {st.Essoufflement}/{maxPe}", GUILayout.Width(130));
            float wantPe = GUILayout.HorizontalSlider(st.Essoufflement, 0f, maxPe);
            if (Mathf.Abs(wantPe - st.Essoufflement) > 0.5f) _arena?.SetDebugPE(unit, Mathf.RoundToInt(wantPe));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool fCrit = DiceRoller.DevForceCritical;
            bool wantCrit = GUILayout.Toggle(fCrit, "🎲 Forcer critique");
            if (wantCrit != fCrit)
            {
                if (wantCrit) DiceRoller.DevForceFumble = false;
                DiceRoller.DevForceCritical = wantCrit;
                _arena?.Log(wantCrit ? "🎲 <b>Rig dev</b> : critiques forcés (restaurer après le test)." : "🎲 <b>Rig dev</b> : critiques forcés désactivés.");
            }
            bool fFumb = DiceRoller.DevForceFumble;
            bool wantFumb = GUILayout.Toggle(fFumb, "💀 Forcer raté");
            if (wantFumb != fFumb)
            {
                if (wantFumb) DiceRoller.DevForceCritical = false;
                DiceRoller.DevForceFumble = wantFumb;
                _arena?.Log(wantFumb ? "💀 <b>Rig dev</b> : ratés forcés (restaurer après le test)." : "💀 <b>Rig dev</b> : ratés forcés désactivés.");
            }
            if (GUILayout.Button("🎲 RNG", GUILayout.Width(64)))
            {
                DiceRoller.ClearDevRig();
                _arena?.Log("🎲 <b>Rig dev</b> : RNG normal restauré.");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🔧 Enrayer arme")) _arena?.ForceDebugJam(unit, true);
            if (GUILayout.Button("🔧 Désenrayer")) _arena?.ForceDebugJam(unit, false);
            GUILayout.EndHorizontal();

            var flagValues = new[] { StatusEffect.ATerre, StatusEffect.Immobilise, StatusEffect.Etourdi, StatusEffect.Aveugle, StatusEffect.Souffrant };
            var flagLabels = new[] { "À Terre", "Immobilisé", "Étourdi", "Aveugle", "Souffrant" };
            GUILayout.BeginHorizontal();
            for (int f = 0; f < flagValues.Length; f++)
            {
                bool isOn = st.ActiveStatus.HasFlag(flagValues[f]);
                bool wantOn = GUILayout.Toggle(isOn, flagLabels[f]);
                if (wantOn != isOn) _arena?.SetDebugStatus(unit, flagValues[f], wantOn);
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("🧹 Purger statuts cible")) _arena?.ClearDebugStatuses(unit);

            GUILayout.Space(4);
            GUILayout.Label("🌊 Purge chrono / zones :");
            GUILayout.BeginHorizontal();
            var branchIds = new[] { TimelineId.Timeline0_Prime, TimelineId.TimelineA_Alpha, TimelineId.TimelineB_Beta, TimelineId.TimelineC_Gamma, TimelineId.TimelineD_Delta };
            var branchShort = new[] { "0", "A", "B", "C", "D" };
            for (int b = 0; b < branchIds.Length; b++)
            {
                int n = _arena != null ? _arena.GetChronoBranchCount(branchIds[b]) : 0;
                if (GUILayout.Button($"{branchShort[b]} ({n})", GUILayout.Height(24))) _arena?.PurgeChronoBranch(branchIds[b]);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🌊 Tout purger (chrono)")) _arena?.PurgeAllChrono();
            if (GUILayout.Button("🌫️ Purger zones")) _arena?.PurgeDebugZones();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawNamedZonesSection()
        {
            if (_arena == null) return;

            GUILayout.Space(8);
            GUILayout.Label("<b>🌫️ Zones Nommées (RD-188, dev-only, sans grenade) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            HexCoordinates poseAt = _arena.CurrentTarget != null
                ? _arena.CurrentTarget.CurrentCoords
                : (_arena.PlayerUnit != null ? _arena.PlayerUnit.CurrentCoords : new HexCoordinates(0, 0));
            GUILayout.Label($"<i>Pose au point ciblé ({poseAt.Q},{poseAt.R}) — cible active ou joueur. Durées/overlays = catalogue.</i>");

            var profiles = Killtime.Core.Combat.NamedZoneProfiles.All;
            for (int row = 0; row < profiles.Count; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < 3 && row + c < profiles.Count; c++)
                {
                    var p = profiles[row + c];
                    int n = 0;
                    var zones = _arena.SmokeZones;
                    if (zones != null)
                        for (int z = 0; z < zones.Count; z++)
                            if (string.Equals(zones[z].Kind ?? "", p.Kind ?? "", StringComparison.OrdinalIgnoreCase)) n++;
                    if (GUILayout.Button($"➕ {p.DisplayName} ({n})", GUILayout.Height(26)))
                        _arena.PoseDebugZone(p.Key, poseAt);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.Label("Purge sélective par zone :");
            for (int row = 0; row < profiles.Count; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < 3 && row + c < profiles.Count; c++)
                {
                    var p = profiles[row + c];
                    if (GUILayout.Button($"🧹 {p.Key}", GUILayout.Height(24)))
                        _arena.PurgeDebugZoneByKind(p.Kind);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
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

            DrawObjectiveParamsSection();

            GUILayout.EndVertical();
        }
    }
}

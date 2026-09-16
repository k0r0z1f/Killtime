using System;
using UnityEngine;
using Killtime.Core.Rules;
using Killtime.Core.Combat;

namespace Killtime.UI
{
    /// <summary>
    /// Tableau de bord développeur interactif pour piloter et ajuster
    /// en temps réel l'ensemble des constantes mathématiques et mécaniques du jeu.
    /// </summary>
    public class CoreRulesDevTableWindow : FloatingWindow<CoreRulesDevTableWindow>
    {
        protected override int WindowId => 995;
        protected override string Title => "Règles & Constantes";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(80, 128, 620, 680);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F3 };
        private static readonly Vector2 _minSize = new Vector2(360, 220);
        private Vector2 _scrollPos;
        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "❤️ Vitalité & PA", "⚔️ Combat & VATS", "📈 Progression", "💾 Presets" };
        private string _statusMsg = "Constantes du Codex actives.";
        private float _pendingCoreSaveTime = -1f;
        private const float CoreAutoSaveDelay = 1.0f;

        protected override void OnUpdate()
        {
            if (_pendingCoreSaveTime > 0f && Time.realtimeSinceStartup >= _pendingCoreSaveTime)
            {
                FlushPendingCoreSave();
            }
        }

        protected override void OnClosed()
        {
            FlushPendingCoreSave();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            FlushPendingCoreSave();
        }

        private void ScheduleCoreAutoSave()
        {
            _pendingCoreSaveTime = Time.realtimeSinceStartup + CoreAutoSaveDelay;
        }

        private void FlushPendingCoreSave()
        {
            if (_pendingCoreSaveTime < 0f) return;
            _pendingCoreSaveTime = -1f;
            try
            {
                var cfg = CoreRulesConfig.Instance;
                cfg.SaveToDisk();
                cfg.PropagateLiveChanges();
                _statusMsg = "Modifications auto-sauvegardées sur disque (rechargées au prochain lancement).";
            }
            catch (Exception e)
            {
                _statusMsg = $"Échec auto-sauvegarde : {e.Message}";
            }
        }

        protected override void DrawContent()
        {
            GUILayout.Space(6);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            GUILayout.Space(6);

            var cfg = CoreRulesConfig.Instance;

            GUILayout.BeginHorizontal();
            GUI.color = Color.cyan;
            GUILayout.Label($"ℹ️ {_statusMsg}");
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.4f);
            if (GUILayout.Button("⚡ Propager aux Unités", GUILayout.Width(160)))
            {
                FlushPendingCoreSave();
                cfg.PropagateLiveChanges();
                _statusMsg = "Constantes propagées et métriques recalculées.";
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case 0: DrawVitalityAndAPTab(cfg); break;
                case 1: DrawCombatAndVATSTab(cfg); break;
                case 2: DrawProgressionAndArcanotechTab(cfg); break;
                case 3: DrawPresetsAndDiskTab(cfg); break;
            }

            GUILayout.EndScrollView();

            if (GUI.changed && _selectedTab != 3)
            {
                ScheduleCoreAutoSave();
                _statusMsg = "Modifications détectées… auto-sauvegarde dans 1 s (conservées au redémarrage).";
            }
        }

        private void DrawVitalityAndAPTab(CoreRulesConfig cfg)
        {
            GUILayout.Label("<b>1. Seuils Vitaux & Multiplicateurs de Constitution (Livre I) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            cfg.EncaissementMultiplier = DrawIntSlider("Multiplicateur Seuil d'Encaissement (CON ×)", cfg.EncaissementMultiplier, 1, 6);
            cfg.LethalMultiplier = DrawIntSlider("Multiplicateur Seuil de Mort (CON ×)", cfg.LethalMultiplier, 2, 10);
            cfg.AttributeMin = DrawIntSlider("Plancher Vital d'un Attribut", cfg.AttributeMin, 0, 5);
            cfg.AttributeMax = DrawIntSlider("Plafond Absolu d'un Attribut", cfg.AttributeMax, 5, 20);
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Économie des Points d'Action & Souffle (Livre I & VI) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            cfg.EmergencyBreathBonusAP = DrawIntSlider("PA Gagnés par Souffle d'Urgence", cfg.EmergencyBreathBonusAP, 1, 5);
            cfg.EmergencyBreathEssoufflementCost = DrawIntSlider("Coût en Essoufflement par Souffle", cfg.EmergencyBreathEssoufflementCost, 1, 3);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Ratio PA en Dernier Souffle : {cfg.LastBreathAPRatio:0.0%}", GUILayout.Width(280));
            cfg.LastBreathAPRatio = GUILayout.HorizontalSlider(cfg.LastBreathAPRatio, 0.1f, 1.0f);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>3. Déplacement & Altérations (Livre VII) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            cfg.BaseMovementAPCost = DrawIntSlider("Coût PA Déplacement Standard / Case", cfg.BaseMovementAPCost, 1, 4);
            cfg.RalentiAPMultiplier = DrawIntSlider("Multiplicateur de Coût d'Action si Ralenti", cfg.RalentiAPMultiplier, 1, 4);
            GUILayout.EndVertical();
        }

        private void DrawCombatAndVATSTab(CoreRulesConfig cfg)
        {
            GUILayout.Label("<b>1. Passes d'Armes & Coûts d'Action de Base (Livre VI) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            cfg.BaseAttackAPCost = DrawIntSlider("Coût en PA Attaque Standard", cfg.BaseAttackAPCost, 1, 6);
            cfg.CancelAimPenaltyAPCost = DrawIntSlider("Coût en PA Compensation Visée Chirurgicale", cfg.CancelAimPenaltyAPCost, 1, 4);
            cfg.BaseReactionAPCost = DrawIntSlider("Coût en PA Réaction / Parade de Base", cfg.BaseReactionAPCost, 0, 4);
            cfg.UnreactiveDefensePenalty = DrawIntSlider("Malus Défenseur sans PA de Réaction", cfg.UnreactiveDefensePenalty, -6, 0);
            cfg.StandardTargetDC = DrawIntSlider("Seuil de Difficulté par Défaut (SD)", cfg.StandardTargetDC, 5, 25);
            cfg.SingleAttackActionLimit = DrawIntSlider("Attaques max autorisées (Dés simples)", cfg.SingleAttackActionLimit, 1, 3);
            cfg.MultiDiceAttackActionLimit = DrawIntSlider("Attaques max autorisées (Dés doubles 2d6+)", cfg.MultiDiceAttackActionLimit, 1, 4);
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Matrice Anatomique VATS (Livre VI, Chap. 26) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            for (int i = 0; i < cfg.BodyPartRules.Count; i++)
            {
                var entry = cfg.BodyPartRules[i];
                var info = BodyPartInfo.GetInfo(entry.Part);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{info.DisplayName}</b>", GUILayout.Width(160));
                GUILayout.Label($"Malus: {entry.DifficultyModifier:+0;-0;0}", GUILayout.Width(70));
                entry.DifficultyModifier = (int)GUILayout.HorizontalSlider(entry.DifficultyModifier, -6, 0, GUILayout.Width(100));

                GUILayout.Label($"Crit: ×{entry.CriticalDamageMultiplier}", GUILayout.Width(65));
                entry.CriticalDamageMultiplier = (int)GUILayout.HorizontalSlider(entry.CriticalDamageMultiplier, 1, 5, GUILayout.Width(100));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawProgressionAndArcanotechTab(CoreRulesConfig cfg)
        {
            GUILayout.Label("<b>1. Coûts de Développement du Personnage (Livre I) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            cfg.XPCostTraining = DrawIntSlider("Coût XP par Niveau de Compétence (+1 Dé)", cfg.XPCostTraining, 1, 20);
            cfg.XPCostSpecialization = DrawIntSlider("Coût XP Déblocage Spécialisation", cfg.XPCostSpecialization, 1, 20);
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Arcanotech & Cinquième Force (Livre IV) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            cfg.ArcanotechResonancePerStageMultiplier = DrawIntSlider("Résonance Nytharite Requise (Par Stade ×)", cfg.ArcanotechResonancePerStageMultiplier, 1, 5);
            GUILayout.Label("<i>Règle d'or inviolable du Codex : Coût de Création en XP = Coût d'Activation en Combat en PA (1:1).</i>");
            GUILayout.EndVertical();
        }

        private void DrawPresetsAndDiskTab(CoreRulesConfig cfg)
        {
            GUILayout.Label("<b>Gestion de la Configuration :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("<i>Autosauvegarde active : chaque réglage des onglets est enregistré sur disque ~1 s après modification et rechargé au lancement.</i>");

            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.4f);
            if (GUILayout.Button("💾 Sauvegarder la Configuration sur Disque", GUILayout.Height(32)))
            {
                _pendingCoreSaveTime = -1f;
                cfg.SaveToDisk();
                cfg.PropagateLiveChanges();
                _statusMsg = "Configuration enregistrée sur disque.";
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("↺ Rétablir les Valeurs Officielles du Codex", GUILayout.Height(32)))
            {
                _pendingCoreSaveTime = -1f;
                cfg.ResetToCodexDefaults();
                cfg.SaveToDisk();
                cfg.PropagateLiveChanges();
                _statusMsg = "Toutes les constantes sont réinitialisées selon les 11 Livres du Codex.";
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>Préférences des Dev UI (positions, onglets, IA, arène, éditeurs) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("💾 Sauver les Préférences Dev", GUILayout.Height(28)))
            {
                try
                {
                    DevUIPreferences.SaveNow();
                    _statusMsg = "Préférences Dev UI enregistrées (DevUIPreferences.json).";
                }
                catch (Exception e)
                {
                    _statusMsg = $"Échec sauvegarde prefs Dev : {e.Message}";
                }
            }
            if (GUILayout.Button("↺ Réinitialiser les Préférences Dev", GUILayout.Height(28)))
            {
                try
                {
                    DevUIPreferences.ResetToDefaults();
                    _statusMsg = "Préférences Dev UI réinitialisées (redémarrez ou rouvrez les fenêtres).";
                }
                catch (Exception e)
                {
                    _statusMsg = $"Échec reset prefs Dev : {e.Message}";
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private int DrawIntSlider(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} : <b>{value}</b>", GUILayout.Width(280));
            value = (int)GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
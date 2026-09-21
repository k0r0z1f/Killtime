using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Multi;
using Killtime.Story;

namespace Killtime.UI
{
    /// <summary>
    /// Barre d'outils et banc d'essai de développement pour le combat tactique.
    /// Conforme à l'Axiome Fondateur du Codex : Tout jet découle d'une compétence.
    /// </summary>
    public class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        protected override int WindowId => 999;
        protected override string Title => "Dev Arena — Combat & VATS";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(20f, 100f, 560f, Mathf.Min(700f, Screen.height - 116f));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.BackQuote, KeyCode.Tab };
        private static readonly Vector2 _minSize = new Vector2(360, 220);

        /// <summary>
        /// Bouton de réouverture quand la fenêtre est fermée : sous la carte joueur HUD
        /// (carte en 24,22,340x62 → bas à y=84, zone d'exclusion jusqu'à 88),
        /// donc y=96 garantit 8px sans chevauchement.
        /// </summary>
        private static Rect ClosedButtonRect => new Rect(24f, 96f, 180f, 28f);

        [Header("Contrôleur")]
        [SerializeField] private CombatDevArena _arena;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CinematicDirector _cinematicDirector;

        private readonly List<string> _logs = new();
        private Vector2 _logScroll;
        private Vector2 _toolsScroll;
        private bool _scrollLock = false;
        // Même pattern robuste que CombatHUD : la demande de retour en bas est
        // consommée dans le Draw, juste avant le BeginScrollView. Quand le verrou
        // est actif, _logScroll n'est jamais modifié : la lecture reste figée.
        private bool _logScrollToBottomPending = false;

        private BodyPart _selectedPart = BodyPart.Torse;
        private bool _cancelPenaltyWithAP = false;
        private int _attackerBonusAP = 0;
        private int _defenderBonusAP = 0;
        private int _attackerPE = 0;
        private int _defenderPE = 0;
        private SkillType _attackSkill = SkillType.Ballistique;
        private SkillType _defenseSkill = SkillType.Esquive;
        private bool _defenderWantsToDefend = true;
        private int _weaponDamage = 5;

        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "🎯 Tir Ciblé (VATS)", "🛠️ Outils & Cheats", "⏳ Chronomancie", "📜 Logs" };

        protected override void Awake()
        {
            base.Awake();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_cinematicDirector == null) _cinematicDirector = FindAnyObjectByType<CinematicDirector>();

            try { LoadDevPrefs(); } catch { /* prefs optionnelles */ }

            if (_arena != null)
            {
                _arena.OnCombatLogMessage += AddLog;
            }

            // Bloque les clics/déplacements/zoom 3D quand la souris survole la fenêtre (ou le bouton de réouverture).
            // Bouton fermé sous la carte joueur HUD : voir ClosedButtonRect (sans chevauchement).
            FloatingWindowChrome.RegisterWindow(999,
                () => _isOpen ? _windowRect : ClosedButtonRect,
                () => isActiveAndEnabled);
        }

        protected override void OnOpened()
        {
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void OnClosed()
        {
            try { CaptureDevPrefs(); DevUIPreferences.SaveNow(); } catch { /* ignore */ }
        }

        private void LoadDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            int partCount = Enum.GetValues(typeof(BodyPart)).Length;
            _selectedPart = (BodyPart)Mathf.Clamp(p.VatsSelectedPart, 0, Math.Max(0, partCount - 1));
            _cancelPenaltyWithAP = p.VatsCancelPenalty;
            _attackerBonusAP = Mathf.Clamp(p.VatsAttackerBonus, 0, 10);
            _defenderBonusAP = Mathf.Clamp(p.VatsDefenderBonus, 0, 10);
            _attackSkill = SafeSkill(p.VatsAttackSkill, SkillType.Ballistique);
            _defenseSkill = SafeSkill(p.VatsDefenseSkill, SkillType.Esquive);
            _defenderWantsToDefend = p.VatsDefenderWantsToDefend;
            _weaponDamage = Mathf.Clamp(p.VatsWeaponDamage, 1, 30);
            _selectedTab = Mathf.Clamp(p.CombatToolbarTab, 0, _tabNames.Length - 1);
            _scrollLock = p.CombatLogScrollLock;
            Application.runInBackground = p.RunInBackground;
        }

        private static SkillType SafeSkill(int raw, SkillType fallback)
        {
            try
            {
                if (Enum.IsDefined(typeof(SkillType), raw)) return (SkillType)raw;
            }
            catch { /* ignore */ }
            return fallback;
        }

        private void CaptureDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            p.VatsSelectedPart = (int)_selectedPart;
            p.VatsCancelPenalty = _cancelPenaltyWithAP;
            p.VatsAttackerBonus = _attackerBonusAP;
            p.VatsDefenderBonus = _defenderBonusAP;
            p.VatsAttackSkill = (int)_attackSkill;
            p.VatsDefenseSkill = (int)_defenseSkill;
            p.VatsDefenderWantsToDefend = _defenderWantsToDefend;
            p.VatsWeaponDamage = _weaponDamage;
            p.CombatToolbarTab = _selectedTab;
            p.CombatLogScrollLock = _scrollLock;
            p.RunInBackground = Application.runInBackground;
            if (_arena != null)
            {
                p.InfiniteAP = _arena.InfiniteAP;
                p.ArenaLayout = (int)_arena.CurrentLayout;
                try { p.EnableCinematicKillcam = _arena.EnableCinematicKillcam; } catch { /* ignore */ }
                try { p.ShowCoverLineOfSight = _arena.ShowCoverLineOfSight; } catch { /* ignore */ }
            }
            try
            {
                var ai = (_arena != null ? _arena.AIController : null) ?? FindAnyObjectByType<TacticalAIController>();
                if (ai != null)
                {
                    p.AiMode = (int)ai.Mode;
                    p.AiEnabled = ai.IsAIEnabled;
                    p.AiActionDelay = ai.ActionDelay;
                    try
                    {
                        p.AiPersonality = (int)ai.DefaultPersonality;
                        p.AiRetreatRatio = ai.RetreatHealthRatio;
                        p.AiDefensiveReserve = ai.DefensiveAPReserve;
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
            DevUIPreferences.MarkDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            FloatingWindowChrome.RegisterWindow(999,
                () => _isOpen ? _windowRect : ClosedButtonRect,
                () => isActiveAndEnabled);
        }

        private void LateUpdate()
        {
            // Les fenêtres dev ne sont pas dans la scène : aucun Update ne les écoute
            // tant qu'elles n'ont pas été créées via Open(). On les crée+ouvre ici à la première
            // pression ; une fois existantes et actives, leur propre Update gère le toggle tout seul.
            // LateUpdate tourne après tous les Update : pas de double-toggle le même frame.
            if (Input.GetKeyDown(KeyCode.F1)) EnsureDevWindow(CharacterDevWindow.Instance, CharacterDevWindow.Open);
            else if (Input.GetKeyDown(KeyCode.F2)) EnsureDevWindow(MapEditorDevWindow.Instance, MapEditorDevWindow.Open);
            else if (Input.GetKeyDown(KeyCode.F3)) EnsureDevWindow(CoreRulesDevTableWindow.Instance, CoreRulesDevTableWindow.Open);
            else if (Input.GetKeyDown(KeyCode.F4)) EnsureDevWindow(VTTRoomWindow.Instance, VTTRoomWindow.Open);
            else if (Input.GetKeyDown(KeyCode.F5) || Input.GetKeyDown(KeyCode.I)) EnsureDevWindow(InventoryDevWindow.Instance, InventoryDevWindow.Open);
            else if (Input.GetKeyDown(KeyCode.F6)) EnsureDevWindow(Killtime.Multi.Video.VTTVideoRoomWindow.Instance, Killtime.Multi.Video.VTTVideoRoomWindow.Open);
            else if (Input.GetKeyDown(KeyCode.F7)) EnsureDevWindow(ScenarioDevWindow.Instance, ScenarioDevWindow.Open);
            else if (Input.GetKeyDown(KeyCode.F8)) EnsureDevWindow(ScenarioEditorDevWindow.Instance, ScenarioEditorDevWindow.Open);
            else if (Input.GetKeyDown(KeyCode.F10)) EnsureDevWindow(WeaponGripEditorDevWindow.Instance, WeaponGripEditorDevWindow.Open);
        }

        /// <summary>
        /// Crée+ouvre une fenêtre dev si elle n'existe pas encore (ou si désactivée).
        /// Si elle existe et est active, son propre Update a déjà traité la touche : ne rien faire.
        /// </summary>
        private static void EnsureDevWindow(MonoBehaviour instance, System.Action open)
        {
            if (instance == null)
            {
                open();
            }
            else if (!instance.isActiveAndEnabled)
            {
                instance.gameObject.SetActive(true);
                instance.enabled = true;
                open();
            }
        }

        public void AddLog(string msg)
        {
            _logs.Add(msg);
            if (_logs.Count > 50) _logs.RemoveAt(0);
            // Verrou actif => ne jamais toucher au scroll. Sinon on reporte la
            // demande au Draw (hauteur réelle connue là-bas).
            if (!_scrollLock)
            {
                _logScrollToBottomPending = true;
            }
        }

        protected override void DrawClosedState()
        {
            if (GUI.Button(ClosedButtonRect, "🛠️ Ouvrir Dev Arena"))
            {
                OpenInstance();
            }
        }

        protected override void DrawContent()
        {
            var activeUnit = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);

            DrawUnitStatusHeader(activeUnit);

            GUILayout.Space(6);
            int newTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            if (newTab != _selectedTab)
            {
                _selectedTab = newTab;
                // En revenant sur les logs en auto-scroll, on repart en bas.
                if (_selectedTab == 3 && !_scrollLock) _logScrollToBottomPending = true;
            }
            GUILayout.Space(8);

            // L'onglet Logs a son propre scroll interne : on ne l'imbrique PAS dans
            // le scroll vertical des outils, sinon la molette pilote les deux et le
            // verrou semble ne pas bloquer l'auto-scroll.
            if (_selectedTab == 3)
            {
                DrawLogsTab();
                if (GUI.changed)
                {
                    try { CaptureDevPrefs(); } catch { /* ignore */ }
                }
                return;
            }

            _toolsScroll = GUILayout.BeginScrollView(_toolsScroll);

            switch (_selectedTab)
            {
                case 0:
                    DrawTargetedAttackTab(activeUnit);
                    break;
                case 1:
                    DrawCheatsAndToolsTab();
                    break;
                case 2:
                    DrawChronomancyTab();
                    break;
            }

            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                try { CaptureDevPrefs(); } catch { /* ignore */ }
            }
        }

        private void DrawUnitStatusHeader(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{unit.Stats.Name}</b> (Round {_turnManager.CurrentRound})", GUILayout.Width(220));

            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ PA: <b>{unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints}</b>");

            GUI.color = Color.green;
            GUILayout.Label($"❤️ PV: <b>{unit.Stats.CurrentHealth}/{unit.Stats.MaxHealth}</b>");
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"🛡️ Encaissement: {unit.Stats.EncaissementThreshold} | Armure: {unit.Stats.BaseArmorAbsorption}", GUILayout.Width(240));

            if (unit.Stats.Essoufflement > 0)
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"🫁 Essoufflement: {unit.Stats.Essoufflement}");
                GUI.color = Color.white;
            }

            if (GUILayout.Button("📜 Fiche", GUILayout.Width(70)))
            {
                CharacterDevWindow.OpenForUnit(unit);
            }

            if (GUILayout.Button("🎒 Sac", GUILayout.Width(60)))
            {
                InventoryDevWindow.Open();
                InventoryDevWindow.Instance?.InspectUnit(unit);
            }

            if (GUILayout.Button("⏭️ Fin du Tour", GUILayout.Width(110)))
            {
                _turnManager?.EndCurrentTurn();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawTargetedAttackTab(TacticalUnit activeUnit)
        {
            GUILayout.Label("<b>1. Sélection de la Cible :</b>");
            GUILayout.BeginHorizontal();

            if (_arena != null)
            {
                foreach (var dummy in _arena.SparringDummies)
                {
                    bool isSelected = (_arena.CurrentTarget == dummy);
                    GUI.backgroundColor = isSelected ? new Color(0.9f, 0.3f, 0.3f) : Color.white;
                    if (GUILayout.Button($"{dummy.Stats.Name}\n({dummy.Stats.CurrentHealth} PV)", GUILayout.Height(38)))
                    {
                        _arena.SelectTarget(dummy);
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndHorizontal();

            var target = _arena != null ? _arena.CurrentTarget : null;
            if (target != null && target.Stats != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Cible active : <b>{target.Stats.Name}</b> | PV: {target.Stats.CurrentHealth}/{target.Stats.MaxHealth} | Encaissement: {target.Stats.EncaissementThreshold} | Armure: {target.Stats.BaseArmorAbsorption} | Agi: {target.Stats.Attributes.Agilite}");
                if (GUILayout.Button("📜 Voir Fiche", GUILayout.Width(95)))
                {
                    CharacterDevWindow.OpenForUnit(target);
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>2. Anatomie Ciblée (Livre VI, Chap. 26) :</b>");

            GUILayout.BeginVertical(GUI.skin.box);
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
            {
                var info = BodyPartInfo.GetInfo(part);
                string desc = $"{info.DisplayName} [Malus: {info.DifficultyModifier} | Crit: x{info.CriticalDamageMultiplier}]";

                bool isChosen = (_selectedPart == part);
                if (GUILayout.Toggle(isChosen, desc))
                {
                    _selectedPart = part;
                }
            }
            GUILayout.EndVertical();

            _cancelPenaltyWithAP = GUILayout.Toggle(_cancelPenaltyWithAP, "Dépenser +1 PA pour annuler le malus de visée");

            GUILayout.Space(6);
            GUILayout.Label("<b>3. Compétences d'Attaque & Défense (Axiome du Codex) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Compétence Attaque:", GUILayout.Width(130));
            if (GUILayout.Toggle(_attackSkill == SkillType.Ballistique, "Ballistique / Tir")) _attackSkill = SkillType.Ballistique;
            if (GUILayout.Toggle(_attackSkill == SkillType.ManiementArmes, "Maniement d'Arme")) _attackSkill = SkillType.ManiementArmes;
            if (GUILayout.Toggle(_attackSkill == SkillType.MainsNues, "Mains Nues")) _attackSkill = SkillType.MainsNues;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Défense Cible:", GUILayout.Width(130));
            if (GUILayout.Toggle(_defenseSkill == SkillType.Esquive, "Esquive")) _defenseSkill = SkillType.Esquive;
            if (GUILayout.Toggle(_defenseSkill == SkillType.DefenseCorporelle, "Défense Corporelle")) _defenseSkill = SkillType.DefenseCorporelle;
            if (GUILayout.Toggle(_defenseSkill == SkillType.ManiementArmes, "Parade Arme")) _defenseSkill = SkillType.ManiementArmes;
            GUILayout.EndHorizontal();

            _defenderWantsToDefend = GUILayout.Toggle(_defenderWantsToDefend, "Cible réactive (tente de parer/esquiver) / Décocher si passive (0 PA, aucune défense)");

            DiceType attDie = activeUnit != null ? activeUnit.Stats.GetSkillDie(_attackSkill, true) : DiceType.D6;
            DiceType defDie = target != null ? target.Stats.GetSkillDie(_defenseSkill) : DiceType.D4;
            int attMod = activeUnit != null ? activeUnit.Stats.GetSkillModifier(_attackSkill, isOffensive: true) : 0;
            int defMod = target != null ? target.Stats.GetSkillModifier(_defenseSkill, isOffensive: false) : 0;
            string attStatus = activeUnit != null ? activeUnit.Stats.GetStatusBreakdownString(_attackSkill, isOffensive: true) : "";
            string defStatus = target != null ? target.Stats.GetStatusBreakdownString(_defenseSkill, isOffensive: false) : "";
            string attStatusDisplay = !string.IsNullOrEmpty(attStatus) ? $" [{attStatus}]" : "";
            string defStatusDisplay = !string.IsNullOrEmpty(defStatus) ? $" [{defStatus}]" : "";

            GUILayout.Label($"<color=cyan>Attaquant : {SkillDefinitions.GetDisplayName(_attackSkill)} ➔ Dé : {attDie} (États: {attMod:+0;-0;0}{attStatusDisplay})</color> | <color=orange>Défenseur : {(_defenderWantsToDefend ? $"{SkillDefinitions.GetDisplayName(_defenseSkill)} ➔ Dé : {defDie} (États: {defMod:+0;-0;0}{defStatusDisplay})" : "Sans Défense (0)")}</color>");
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Dégâts Bruts de l'Arme : {_weaponDamage}", GUILayout.Width(200));
            _weaponDamage = (int)GUILayout.HorizontalSlider(_weaponDamage, 1, 20);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>4. Mises du duel aveugle (PA + PE, Livres II §7 + VI §24.1) :</b>");
            GUILayout.Label("<i>Déclarations masquées avant les jets : attaquant puis défenseur (aveugle), révélation simultanée. 1 PA = +1, 1 PE = +1 (plafond Constitution).</i>");

            int baseCost = _cancelPenaltyWithAP ? 3 : 2;
            int currentAttackerAP = activeUnit != null ? activeUnit.Stats.CurrentActionPoints : 0;
            int maxAttackerBonus = Mathf.Max(0, currentAttackerAP - baseCost);
            _attackerBonusAP = Mathf.Clamp(_attackerBonusAP, 0, maxAttackerBonus);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"⚡ Mise Attaquant (aveugle) : <b>+{_attackerBonusAP} PA</b>", GUILayout.Width(260));
            _attackerBonusAP = (int)GUILayout.HorizontalSlider(_attackerBonusAP, 0, maxAttackerBonus);
            GUILayout.EndHorizontal();

            int maxAttackerPE = activeUnit != null ? activeUnit.Stats.GetSpendablePE() : 0;
            _attackerPE = Mathf.Clamp(_attackerPE, 0, maxAttackerPE);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"🫁 PE Attaquant (aveugle) : <b>+{_attackerPE} PE</b> (ESS {activeUnit?.Stats.Essoufflement ?? 0}/{activeUnit?.Stats.Attributes.Constitution ?? 0})", GUILayout.Width(260));
            _attackerPE = (int)GUILayout.HorizontalSlider(_attackerPE, 0, maxAttackerPE);
            GUILayout.EndHorizontal();

            int currentDefenderAP = target != null ? target.Stats.CurrentActionPoints : 0;
            int maxDefenderBonus = Mathf.Max(0, currentDefenderAP - 1);
            _defenderBonusAP = Mathf.Clamp(_defenderBonusAP, 0, maxDefenderBonus);

            if (_defenderWantsToDefend)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🛡️ Mise Défenseur (aveugle) : <b>+{_defenderBonusAP} PA</b>", GUILayout.Width(260));
                _defenderBonusAP = (int)GUILayout.HorizontalSlider(_defenderBonusAP, 0, maxDefenderBonus);
                GUILayout.EndHorizontal();

                int maxDefenderPE = target != null ? target.Stats.GetSpendablePE() : 0;
                _defenderPE = Mathf.Clamp(_defenderPE, 0, maxDefenderPE);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🫁 PE Défenseur (aveugle) : <b>+{_defenderPE} PE</b> (ESS {target?.Stats.Essoufflement ?? 0}/{target?.Stats.Attributes.Constitution ?? 0})", GUILayout.Width(260));
                _defenderPE = (int)GUILayout.HorizontalSlider(_defenderPE, 0, maxDefenderPE);
                GUILayout.EndHorizontal();
            }

            int maxAttacks = activeUnit != null ? activeUnit.Stats.GetMaxAttacksAllowed(attDie) : 1;
            int attacksDone = activeUnit != null ? activeUnit.Stats.AttacksThisTurn : 0;
            bool canAttack = activeUnit != null && activeUnit.Stats.CanAttack(attDie);

            GUILayout.Space(6);
            GUILayout.Label($"Attaques du tour : <b>{attacksDone} / {maxAttacks}</b> {(maxAttacks > 1 ? "<color=cyan>(Double action active)</color>" : "<color=gray>(Requis 2d6+ pour 2e attaque)</color>")}");

            GUILayout.Space(6);
            GUI.backgroundColor = canAttack ? new Color(0.9f, 0.2f, 0.2f) : Color.gray;
            GUI.enabled = canAttack;
            int totalApCost = baseCost + _attackerBonusAP;
            if (GUILayout.Button(canAttack ? $"🎯 EXÉCUTER L'ATTAQUE CIBLÉE ({totalApCost} PA)" : "⚠️ QUOTA D'ATTAQUE ÉPUISÉ POUR CE TOUR", GUILayout.Height(40)))
            {
                if (_arena != null)
                {
                    _arena.ExecuteAttack(
                        targetedPart: _selectedPart, 
                        cancelPenaltyWithAP: _cancelPenaltyWithAP, 
                        attackSkill: _attackSkill, 
                        defenseSkill: _defenseSkill, 
                        defenderWantsToDefend: _defenderWantsToDefend,
                        weaponBaseDamage: _weaponDamage, 
                        attackerBonusAP: _attackerBonusAP, 
                        defenderBonusAP: _defenderBonusAP,
                        attackerPE: _attackerPE,
                        defenderPE: _defenderWantsToDefend ? _defenderPE : 0
                    );
                }
            }
            GUI.enabled = true;
            GUI.backgroundColor = Color.white;
        }

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

                GUI.enabled = !isPlayer;

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

                GUI.enabled = true;

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

            GUILayout.Space(8);
            GUILayout.Label("<b>🖥️ Système & Multitâche (Alt-Tab / Bureau) :</b>");
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

            GUILayout.Space(8);
            GUILayout.Label("<b>💡 Éclairage & Atmosphère :</b>");
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
            if (GUILayout.Button("🗡️ Ancrage Armes / Grip (F10)", GUILayout.Height(32)))
            {
                WeaponGripEditorDevWindow.Open();
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
            GUILayout.Label("<b>🎬 Campagne narrative :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🎬 Scènes & Choix (Lecteur F7)", GUILayout.Height(32)))
            {
                ScenarioDevWindow.Open();
            }
            if (GUILayout.Button("🛠️ Éditeur de Scènes JSON (F8)", GUILayout.Height(32)))
            {
                ScenarioEditorDevWindow.Open();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>🌐 Multijoueur (Table Virtuelle) :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🌐 Room VTT (F4)", GUILayout.Height(32)))
            {
                VTTRoomWindow.Open();
            }
            if (GUILayout.Button("📹 Salon Vidéo (F6)", GUILayout.Height(32)))
            {
                Killtime.Multi.Video.VTTVideoRoomWindow.Open();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>🎒 Inventaire, Armurerie & Marché :</b>");
            if (GUILayout.Button("🎒🏪 Inventaire / Armurerie / Marché (F5 / I)", GUILayout.Height(32)))
            {
                InventoryDevWindow.Open();
            }
            GUILayout.Space(16);
        }

        private void DrawChronomancyTab()
        {
            GUILayout.Label("<b>⏳ Le Fleuve du Temps (Livre V) — Sauvegardes & Rembobinage :</b>");
            GUILayout.Space(4);

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
            GUILayout.Label("Historique chronologique :");
            if (_arena != null && _arena.Timeline != null)
            {
                var history = _arena.Timeline.GetFullChronology();
                for (int i = history.Count - 1; i >= Mathf.Max(0, history.Count - 6); i--)
                {
                    var s = history[i];
                    GUILayout.Label($"• [Round {s.RoundNumber} / {s.SecondInRound:0.0}s] {s.ActionDescription}");
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

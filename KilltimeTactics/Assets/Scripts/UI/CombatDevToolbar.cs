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

namespace Killtime.UI
{
    /// <summary>
    /// Barre d'outils et banc d'essai de développement pour le combat tactique.
    /// Conforme à l'Axiome Fondateur du Codex : Tout jet découle d'une compétence.
    /// </summary>
    public class CombatDevToolbar : MonoBehaviour
    {
        public static CombatDevToolbar Instance { get; private set; }

        [Header("Contrôleur")]
        [SerializeField] private CombatDevArena _arena;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CinematicDirector _cinematicDirector;

        public static bool IsPointerOverToolbar()
        {
            if (Instance == null || !Instance._isOpen) return false;
            Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return Instance._windowRect.Contains(mouseGui);
        }

        [Header("Affichage")]
        [SerializeField] private bool _isOpen = true;
        [SerializeField] private KeyCode _toggleKey = KeyCode.BackQuote;

        private readonly List<string> _logs = new();
        private Vector2 _logScroll;
        private Vector2 _toolsScroll;
        private bool _scrollLock = false;

        private BodyPart _selectedPart = BodyPart.Torse;
        private bool _cancelPenaltyWithAP = false;
        private int _attackerBonusAP = 0;
        private int _defenderBonusAP = 0;
        private SkillType _attackSkill = SkillType.Ballistique;
        private SkillType _defenseSkill = SkillType.Esquive;
        private bool _defenderWantsToDefend = true;
        private int _weaponDamage = 5;

        private Rect _windowRect = new Rect(20, 20, 560, 720);
        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "🎯 Tir Ciblé (VATS)", "🛠️ Outils & Cheats", "⏳ Chronomancie", "📜 Logs" };

        private void Awake()
        {
            Instance = this;
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_cinematicDirector == null) _cinematicDirector = FindAnyObjectByType<CinematicDirector>();

            if (_arena != null)
            {
                _arena.OnCombatLogMessage += AddLog;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey) || Input.GetKeyDown(KeyCode.Tab))
            {
                _isOpen = !_isOpen;
            }
        }

        public void AddLog(string msg)
        {
            _logs.Add(msg);
            if (_logs.Count > 50) _logs.RemoveAt(0);
            if (!_scrollLock)
            {
                _logScroll.y = float.MaxValue;
            }
        }

        private void OnGUI()
        {
            if (!_isOpen)
            {
                if (GUI.Button(new Rect(20, 20, 180, 32), "🛠️ Ouvrir Dev Arena"))
                {
                    _isOpen = true;
                }
                return;
            }

            _windowRect = GUI.Window(999, _windowRect, DrawWindowContent, "⚔️ Killtime — Combat Development Arena & VATS Sandbox");
        }

        private void DrawWindowContent(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, _windowRect.width - 60, 25));

            if (GUI.Button(new Rect(_windowRect.width - 55, 4, 50, 20), "Réduire"))
            {
                _isOpen = false;
            }

            var activeUnit = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);

            DrawUnitStatusHeader(activeUnit);

            GUILayout.Space(6);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            GUILayout.Space(8);

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
                case 3:
                    DrawLogsTab();
                    break;
            }

            GUILayout.EndScrollView();
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

            DiceType attDie = activeUnit != null ? activeUnit.Stats.GetSkillDie(_attackSkill) : DiceType.D6;
            DiceType defDie = target != null ? target.Stats.GetSkillDie(_defenseSkill) : DiceType.D4;
            int attMod = activeUnit != null ? activeUnit.Stats.GetSkillModifier(_attackSkill, isOffensive: true) : 0;
            int defMod = target != null ? target.Stats.GetSkillModifier(_defenseSkill, isOffensive: false) : 0;
            string attStatus = activeUnit != null ? activeUnit.Stats.GetStatusBreakdownString(_attackSkill, isOffensive: true) : "";
            string defStatus = target != null ? target.Stats.GetStatusBreakdownString(_defenseSkill, isOffensive: false) : "";
            string attStatusDisplay = !string.IsNullOrEmpty(attStatus) ? $" [{attStatus}]" : "";
            string defStatusDisplay = !string.IsNullOrEmpty(defStatus) ? $" [{defStatus}]" : "";

            GUILayout.Label($"<color=cyan>Attaquant : {SkillDefinitions.GetDisplayName(_attackSkill)} ➔ Dé : {attDie} (Mod: {attMod:+0;-0;0}{attStatusDisplay})</color> | <color=orange>Défenseur : {(_defenderWantsToDefend ? $"{SkillDefinitions.GetDisplayName(_defenseSkill)} ➔ Dé : {defDie} (Mod: {defMod:+0;-0;0}{defStatusDisplay})" : "Sans Défense (0)")}</color>");
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Dégâts Bruts de l'Arme : {_weaponDamage}", GUILayout.Width(200));
            _weaponDamage = (int)GUILayout.HorizontalSlider(_weaponDamage, 1, 20);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>4. Injection des Points d'Action (+1 au Jet / PA) :</b>");

            int baseCost = _cancelPenaltyWithAP ? 3 : 2;
            int currentAttackerAP = activeUnit != null ? activeUnit.Stats.CurrentActionPoints : 0;
            int maxAttackerBonus = Mathf.Max(0, currentAttackerAP - baseCost);
            _attackerBonusAP = Mathf.Clamp(_attackerBonusAP, 0, maxAttackerBonus);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"⚡ PA Bonus Attaquant : <b>+{_attackerBonusAP}</b>", GUILayout.Width(220));
            _attackerBonusAP = (int)GUILayout.HorizontalSlider(_attackerBonusAP, 0, maxAttackerBonus);
            GUILayout.EndHorizontal();

            int currentDefenderAP = target != null ? target.Stats.CurrentActionPoints : 0;
            int maxDefenderBonus = Mathf.Max(0, currentDefenderAP - 1);
            _defenderBonusAP = Mathf.Clamp(_defenderBonusAP, 0, maxDefenderBonus);

            if (_defenderWantsToDefend)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🛡️ PA Bonus Défenseur : <b>+{_defenderBonusAP}</b>", GUILayout.Width(220));
                _defenderBonusAP = (int)GUILayout.HorizontalSlider(_defenderBonusAP, 0, maxDefenderBonus);
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
                        defenderBonusAP: _defenderBonusAP
                    );
                }
            }
            GUI.enabled = true;
            GUI.backgroundColor = Color.white;
        }

        private void DrawCheatsAndToolsTab()
        {
            var ai = _arena != null ? _arena.AIController : null;
            if (ai != null)
            {
                GUILayout.Label("<b>🤖 Automatisation Tactique (IA) :</b>");
                GUILayout.BeginVertical(GUI.skin.box);

                ai.IsAIEnabled = GUILayout.Toggle(ai.IsAIEnabled, "Activer le Contrôleur d'IA");

                GUILayout.BeginHorizontal();
                GUILayout.Label("Mode IA :", GUILayout.Width(80));

                bool isNormal = (ai.Mode == CombatAIMode.Normal);
                bool isFullAuto = (ai.Mode == CombatAIMode.FullAuto);

                GUI.backgroundColor = isNormal ? new Color(0.2f, 0.6f, 1f) : Color.white;
                if (GUILayout.Button("Mode Normal (Ennemis IA)"))
                {
                    ai.Mode = CombatAIMode.Normal;
                }

                GUI.backgroundColor = isFullAuto ? new Color(1f, 0.4f, 0.2f) : Color.white;
                if (GUILayout.Button("Mode Full Auto (100% IA)"))
                {
                    ai.Mode = CombatAIMode.FullAuto;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Délai d'action : {ai.ActionDelay:0.00}s", GUILayout.Width(150));
                ai.ActionDelay = GUILayout.HorizontalSlider(ai.ActionDelay, 0.05f, 6.0f);
                GUILayout.EndHorizontal();

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
            GUILayout.Label("<b>👥 Personnages & Fiches :</b>");
            if (GUILayout.Button("🧙 Ouvrir le Créateur de Personnage (F1)", GUILayout.Height(32)))
            {
                CharacterDevWindow.Open();
            }

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
            _scrollLock = GUILayout.Toggle(_scrollLock, "🔒 Scroll Lock", GUILayout.Width(110));
            if (GUILayout.Button("Effacer", GUILayout.Width(70)))
            {
                _logs.Clear();
            }
            GUILayout.EndHorizontal();

            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.Height(320));
            for (int i = _logs.Count - 1; i >= 0; i--)
            {
                GUILayout.Label(_logs[i]);
            }
            GUILayout.EndScrollView();
        }
    }
}
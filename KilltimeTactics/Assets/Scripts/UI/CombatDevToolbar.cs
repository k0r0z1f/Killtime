using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.CameraSystem;

namespace Killtime.UI
{
    /// <summary>
    /// Barre d'outils et banc d'essai de développement pour le combat tactique.
    /// Permet de manipuler en temps réel les PA, la vie, les tirs ciblés (VATS),
    /// les layouts d'obstacles, la caméra cinématique et le rembobinage chronomantique.
    /// </summary>
    public class CombatDevToolbar : MonoBehaviour
    {
        [Header("Contrôleur")]
        [SerializeField] private CombatDevArena _arena;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CinematicDirector _cinematicDirector;

        [Header("Affichage")]
        [SerializeField] private bool _isOpen = true;
        [SerializeField] private KeyCode _toggleKey = KeyCode.BackQuote; // Touche '~' ou ²

        private readonly List<string> _logs = new();
        private Vector2 _logScroll;
        private Vector2 _toolsScroll;

        private BodyPart _selectedPart = BodyPart.Torse;
        private bool _cancelPenaltyWithAP = false;
        private int _attackerBonusAP = 0;
        private int _defenderBonusAP = 0;
        private DiceType _attackDie = DiceType.D6;
        private DiceType _defenseDie = DiceType.D4;
        private int _weaponDamage = 5;

        private Rect _windowRect = new Rect(20, 20, 520, 680);
        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "🎯 Tir Ciblé (VATS)", "🛠️ Outils & Cheats", "⏳ Chronomancie", "📜 Logs" };

        private void Awake()
        {
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
            _logScroll.y = float.MaxValue; // Auto-scroll vers le bas
        }

        private void OnGUI()
        {
            // Bouton discret de réouverture si minimisé
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

            // 1. Bandeau supérieur d'état
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
            GUILayout.Label("<b>3. Paramètres des Dés & Arme :</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Dé Attaque:", GUILayout.Width(80));
            _attackDie = (DiceType)GUILayout.Toolbar((int)_attackDie, new[] { "D2", "D3", "D4", "D6", "D8", "D10", "D12", "D20" }, GUILayout.Height(22));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Dé Défense:", GUILayout.Width(80));
            _defenseDie = (DiceType)GUILayout.Toolbar((int)_defenseDie, new[] { "D2", "D3", "D4", "D6", "D8", "D10", "D12", "D20" }, GUILayout.Height(22));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Dégâts Bruts de l'Arme : {_weaponDamage}", GUILayout.Width(200));
            _weaponDamage = (int)GUILayout.HorizontalSlider(_weaponDamage, 1, 20);
            GUILayout.EndHorizontal();

            // 4. Enchère des PA Bonus (Livre VI, Chap. 24)
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

            var currentDefender = _arena != null ? _arena.CurrentTarget : null;
            int currentDefenderAP = currentDefender != null ? currentDefender.Stats.CurrentActionPoints : 0;
            int maxDefenderBonus = Mathf.Max(0, currentDefenderAP - 1);
            _defenderBonusAP = Mathf.Clamp(_defenderBonusAP, 0, maxDefenderBonus);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"🛡️ PA Bonus Défenseur : <b>+{_defenderBonusAP}</b>", GUILayout.Width(220));
            _defenderBonusAP = (int)GUILayout.HorizontalSlider(_defenderBonusAP, 0, maxDefenderBonus);
            GUILayout.EndHorizontal();

            int maxAttacks = activeUnit != null ? activeUnit.Stats.GetMaxAttacksAllowed(_attackDie) : 1;
            int attacksDone = activeUnit != null ? activeUnit.Stats.AttacksThisTurn : 0;
            bool canAttack = activeUnit != null && activeUnit.Stats.CanAttack(_attackDie);

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
                    _arena.ExecuteAttack(_selectedPart, _cancelPenaltyWithAP, _attackDie, _defenseDie, _weaponDamage, _attackerBonusAP, _defenderBonusAP);
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
                ai.ActionDelay = GUILayout.HorizontalSlider(ai.ActionDelay, 0.05f, 1.2f);
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
            if (GUILayout.Button("🔄 Réinitialiser l'Arène"))
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

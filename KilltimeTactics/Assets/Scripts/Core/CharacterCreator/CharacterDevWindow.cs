using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Arcanotech;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    /// <summary>
    /// Créateur & Gestionnaire de Personnages avec auto-instanciation et placement sur la grille.
    /// </summary>
    public class CharacterDevWindow : MonoBehaviour
    {
        public static CharacterDevWindow Instance { get; private set; }

        [Header("Systèmes")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        [Header("Affichage")]
        [SerializeField] private bool _isOpen = false;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F1;

        private CharacterSheet _currentSheet = new();
        private int _selectedTab = 0;
        private readonly string[] _tabTitles = { "👤 Création", "📈 Progression", "🔮 Sorts (XP=PA)", "💾 Disque", "🗺️ Spawner" };

        private Rect _windowRect;
        private Vector2 _scrollPos;
        private string _statusMessage = "Prêt.";
        private string _spawnQStr = "0";
        private string _spawnRStr = "1";
        private bool _spawnAsPlayer = false;

        // Sorts modulaires
        private string _customSpellName = "Onde de Choc Causal";
        private int _customSpellDamage = 6;
        private int _customSpellCost = 3;

        /// <summary>
        /// Ouvre la fenêtre et crée l'objet dans la scène s'il n'existe pas encore.
        /// </summary>
        public static void Open()
        {
            if (Instance == null)
            {
                Instance = FindAnyObjectByType<CharacterDevWindow>();
                if (Instance == null)
                {
                    var go = new GameObject("[UI] CharacterDevWindow");
                    Instance = go.AddComponent<CharacterDevWindow>();
                }
            }

            Instance._isOpen = true;
            Instance.EnsureReferences();
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            EnsureReferences();
            CharacterStorageService.EnsureDirectoryExists();

            // Positionne la fenêtre sur la droite de l'écran
            float width = 660;
            float height = Mathf.Min(760, Screen.height - 40);
            _windowRect = new Rect(Screen.width - width - 20, 20, width, height);
        }

        private void EnsureReferences()
        {
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _isOpen = !_isOpen;
                if (_isOpen) EnsureReferences();
            }
        }

        public void OpenWindow()
        {
            _isOpen = true;
            EnsureReferences();
        }

        private void OnGUI()
        {
            if (!_isOpen) return;

            // Garde la fenêtre visible à l'écran
            _windowRect.height = Mathf.Min(780, Screen.height - 40);
            _windowRect = GUI.Window(888, _windowRect, DrawWindowContent, "⚔️ Killtime — Créateur de Personnages (Héros & PNJ)");
            GUI.BringWindowToFront(888);
        }

        private void DrawWindowContent(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, _windowRect.width - 70, 25));
            if (GUI.Button(new Rect(_windowRect.width - 65, 4, 60, 20), "Fermer"))
            {
                _isOpen = false;
            }

            GUILayout.Space(6);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabTitles);
            GUILayout.Space(6);

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"ℹ️ {_statusMessage}", GUI.skin.box);
                GUI.color = Color.white;
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case 0: DrawCreationTab(); break;
                case 1: DrawProgressionTab(); break;
                case 2: DrawSpellForgeTab(); break;
                case 3: DrawStorageTab(); break;
                case 4: DrawMapInsertionTab(); break;
            }

            GUILayout.EndScrollView();
        }

        // ================= TAB 0 : CRÉATION =================
        private void DrawCreationTab()
        {
            GUILayout.Label("<b>1. Identité & Profil (Livre I) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(90));
            _currentSheet.Name = GUILayout.TextField(_currentSheet.Name);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Âge / Sexe :", GUILayout.Width(90));
            string ageStr = GUILayout.TextField(_currentSheet.Age.ToString(), GUILayout.Width(50));
            if (int.TryParse(ageStr, out int a)) _currentSheet.Age = a;
            _currentSheet.Gender = GUILayout.TextField(_currentSheet.Gender);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Espèce :", GUILayout.Width(90));
            _currentSheet.Species = (SpeciesType)GUILayout.Toolbar((int)_currentSheet.Species, Enum.GetNames(typeof(SpeciesType)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Profil :", GUILayout.Width(90));
            _currentSheet.Profile = (CharacterProfileType)GUILayout.Toolbar((int)_currentSheet.Profile, Enum.GetNames(typeof(CharacterProfileType)));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            int allocatedSecondary = _currentSheet.BaseAttributes.Force + _currentSheet.BaseAttributes.Agilite + 
                                     _currentSheet.BaseAttributes.Constitution + _currentSheet.BaseAttributes.Rapidite + 
                                     _currentSheet.BaseAttributes.Intelligence + _currentSheet.BaseAttributes.Erudition + 
                                     _currentSheet.BaseAttributes.Charisme + _currentSheet.BaseAttributes.Instinct;

            GUILayout.Space(6);
            GUILayout.Label($"<b>2. Les 8 Attributs Fondamentaux (Points alloués : {allocatedSecondary}) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            _currentSheet.BaseAttributes.Force = DrawAttrRow("Force (FOR)", _currentSheet.BaseAttributes.Force);
            _currentSheet.BaseAttributes.Agilite = DrawAttrRow("Agilité (AGI)", _currentSheet.BaseAttributes.Agilite);
            _currentSheet.BaseAttributes.Constitution = DrawAttrRow("Constitution (CON)", _currentSheet.BaseAttributes.Constitution);
            _currentSheet.BaseAttributes.Rapidite = DrawAttrRow("Rapidité (RAP)", _currentSheet.BaseAttributes.Rapidite);
            _currentSheet.BaseAttributes.Intelligence = DrawAttrRow("Intelligence (INT)", _currentSheet.BaseAttributes.Intelligence);
            _currentSheet.BaseAttributes.Erudition = DrawAttrRow("Érudition (ÉRU)", _currentSheet.BaseAttributes.Erudition);
            _currentSheet.BaseAttributes.Charisme = DrawAttrRow("Charisme (CHA)", _currentSheet.BaseAttributes.Charisme);
            _currentSheet.BaseAttributes.Instinct = DrawAttrRow("Instinct (INS)", _currentSheet.BaseAttributes.Instinct);
            _currentSheet.BaseAttributes.Magie = DrawAttrRow("Magie (5e Force)", _currentSheet.BaseAttributes.Magie);

            GUILayout.EndVertical();

            var effective = _currentSheet.GetEffectiveAttributes();
            GUILayout.Space(6);
            GUILayout.Label("<b>3. Métriques Vitales Dérivées (Temps Réel) :</b>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ PA : <b>{effective.CalculateBaseActionPoints()}</b>");
            GUI.color = Color.green;
            GUILayout.Label($"🛡️ Encaissement : <b>{effective.CalculateEncaissement()}</b>");
            GUI.color = Color.red;
            GUILayout.Label($"💀 Seuil Mort : <b>{effective.CalculateLethalMaximum()} PV</b>");
            GUI.color = Color.yellow;
            GUILayout.Label($"🫁 Souffle : <b>{effective.Constitution} tours</b>");
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
            if (GUILayout.Button("💾 Sauvegarder cette fiche sur le Disque", GUILayout.Height(34)))
            {
                string path = CharacterStorageService.SaveCharacter(_currentSheet);
                _statusMessage = $"Fiche '{_currentSheet.Name}' sauvegardée !";
            }
            GUI.backgroundColor = Color.white;
        }

        private int DrawAttrRow(string label, int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(170));
            if (GUILayout.Button("-", GUILayout.Width(30))) value = Mathf.Max(1, value - 1);
            GUILayout.Label($"<b>{value}</b>", GUILayout.Width(35));
            if (GUILayout.Button("+", GUILayout.Width(30))) value++;
            GUILayout.EndHorizontal();
            return value;
        }

        // ================= TAB 1 : PROGRESSION =================
        private void DrawProgressionTab()
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Personnage : <b>{_currentSheet.Name}</b> | XP Disponible : <color=yellow><b>{_currentSheet.AvailableXP} XP</b></color>");
            if (GUILayout.Button("+10 XP", GUILayout.Width(70))) CharacterProgressionManager.GrantXP(_currentSheet, 10);
            if (GUILayout.Button("+50 XP", GUILayout.Width(70))) CharacterProgressionManager.GrantXP(_currentSheet, 50);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>Arbres de Compétences (5 XP = +1 Palier de Dé) :</b>");

            foreach (var entry in _currentSheet.Skills)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{entry.Skill}</b> (Niveau +{entry.TrainingLevel})", GUILayout.Width(250));
                if (GUILayout.Button("Entraîner (+1 Palier / 5 XP)", GUILayout.Width(190)))
                {
                    if (CharacterProgressionManager.TrainSkill(_currentSheet, entry.Skill, out string msg))
                        _statusMessage = msg;
                    else
                        _statusMessage = msg;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Spécialisations (5 XP) :</b>");
            string[] availableSpecs = { "Maniement de l'Épée", "Marteau de Guerre", "Pistolet & Tir Rapide", "Escrime", "Arts Martiaux", "Chirurgie", "Tromper", "Négocier", "Intimider", "Leadership" };

            foreach (var spec in availableSpecs)
            {
                bool owned = _currentSheet.UnlockedSpecializations.Contains(spec);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(spec, GUILayout.Width(230));
                if (owned)
                {
                    GUI.color = Color.green;
                    GUILayout.Label("✅ Acquis");
                    GUI.color = Color.white;
                }
                else
                {
                    if (GUILayout.Button("Débloquer (5 XP)", GUILayout.Width(130)))
                    {
                        if (CharacterProgressionManager.UnlockSpecialization(_currentSheet, spec, out string msg))
                            _statusMessage = msg;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        // ================= TAB 2 : FORGE DE SORTS =================
        private void DrawSpellForgeTab()
        {
            GUILayout.Label("<b>Atelier de Sorts Modulaires (Livre IV — 1 XP = 1 PA) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom du Sort :", GUILayout.Width(110));
            _customSpellName = GUILayout.TextField(_customSpellName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Dégâts : {_customSpellDamage}", GUILayout.Width(110));
            _customSpellDamage = (int)GUILayout.HorizontalSlider(_customSpellDamage, 1, 20);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Coût XP / PA : {_customSpellCost}", GUILayout.Width(110));
            _customSpellCost = (int)GUILayout.HorizontalSlider(_customSpellCost, 1, 10);
            GUILayout.EndHorizontal();

            if (GUILayout.Button($"🔮 Forger & Apprendre ce Sort ({_customSpellCost} XP)", GUILayout.Height(34)))
            {
                var spell = new NythariteSpell(_customSpellName, PsychicDiscipline.Telekinesie, PsychicStage.Stade2_AccesProfond, _customSpellCost, damage: _customSpellDamage);
                if (CharacterProgressionManager.LearnModularSpell(_currentSheet, spell, out string msg))
                    _statusMessage = msg;
                else
                    _statusMessage = msg;
            }

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>Sorts Mémorisés :</b>");
            foreach (var sp in _currentSheet.LearnedSpells)
            {
                GUILayout.Label($"• <b>{sp.Name}</b> — Coût : {sp.ActionPointCost} PA | Dégâts : {sp.BaseArcaneDamage}");
            }
        }

        // ================= TAB 3 : FICHIERS =================
        private void DrawStorageTab()
        {
            GUILayout.Label("<b>Fichiers Sauvegardés sur le Disque :</b>");
            var files = CharacterStorageService.GetSavedCharacterFiles();

            if (files.Count == 0)
            {
                GUILayout.Label("Aucun fichier trouvé.");
            }

            foreach (var f in files)
            {
                string fName = Path.GetFileNameWithoutExtension(f);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(fName, GUILayout.Width(280));

                if (GUILayout.Button("Charger", GUILayout.Width(80)))
                {
                    _currentSheet = CharacterStorageService.LoadCharacter(f);
                    _statusMessage = $"'{_currentSheet.Name}' chargé !";
                }

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Suppr.", GUILayout.Width(60)))
                {
                    CharacterStorageService.DeleteCharacter(f);
                    _statusMessage = "Fichier supprimé.";
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(12);
            if (GUILayout.Button("📋 Copier le JSON de cette fiche dans le Presse-Papier"))
            {
                GUIUtility.systemCopyBuffer = CharacterStorageService.ExportToJson(_currentSheet);
                _statusMessage = "JSON copié !";
            }
        }

        // ================= TAB 4 : SPAWN SUR LA GRILLE =================
        private void DrawMapInsertionTab()
        {
            GUILayout.Label("<b>Insérer le Personnage sur la Grille Hexagonale 3D :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"Personnage actif : <b>{_currentSheet.Name}</b> ({_currentSheet.Species})");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Coordonnée Hexagonale Q :", GUILayout.Width(180));
            _spawnQStr = GUILayout.TextField(_spawnQStr, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Coordonnée Hexagonale R :", GUILayout.Width(180));
            _spawnRStr = GUILayout.TextField(_spawnRStr, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            _spawnAsPlayer = GUILayout.Toggle(_spawnAsPlayer, "Contrôlé par le Joueur (Bleu) / Sinon Ennemi (Rouge)");

            GUILayout.Space(10);
            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f);
            if (GUILayout.Button("⚡ INSERER L'UNITÉ SUR LA GRILLE", GUILayout.Height(38)))
            {
                SpawnCharacterOnGrid();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
        }

        private void SpawnCharacterOnGrid()
        {
            EnsureReferences();

            if (_grid == null)
            {
                _statusMessage = "Erreur : TacticalHexGrid introuvable.";
                return;
            }

            int.TryParse(_spawnQStr, out int q);
            int.TryParse(_spawnRStr, out int r);

            var coords = new HexCoordinates(q, r);
            var node = _grid.GetNode(coords);

            if (node == null)
            {
                _statusMessage = $"Coordonnées ({q}, {r}) hors limites de la grille.";
                return;
            }

            if (node.IsOccupied)
            {
                _statusMessage = $"La case ({q}, {r}) est déjà occupée !";
                return;
            }

            var go = new GameObject($"Unit_{_currentSheet.Name.Replace(" ", "_")}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.InitializeFromSheet(_currentSheet, coords, _grid, _spawnAsPlayer);

            if (_turnManager != null)
            {
                _turnManager.RegisterUnit(unit);
            }

            if (_arena != null && !_spawnAsPlayer)
            {
                _arena.SparringDummies.Add(unit);
            }

            _statusMessage = $"'{_currentSheet.Name}' inséré avec succès en ({q}, {r}) !";
        }
    }
}
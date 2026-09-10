// [CODE MIS À JOUR]
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Interface utilisateur contextuelle apparaissant sous le curseur lors d'un clic droit.
    /// Affiche les actions possibles catégorisées avec consommation de PA en temps réel.
    /// </summary>
    public class CombatContextMenuUI : MonoBehaviour
    {
        [Header("Systèmes")]
        [SerializeField] private TacticalSelectionManager _selectionManager;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        public static CombatContextMenuUI Instance { get; private set; }

        private bool _isVisible = false;
        private Rect _menuRect;
        private TacticalUnit _contextTarget;
        private List<CombatAction> _cachedActions = new();
        private ActionCategory _selectedCategory = ActionCategory.AttaqueEtPassesDarmes;
        private int _openFrame;

        public static bool IsPointerOverMenu(Vector2 mouseScreenPos)
        {
            return Instance != null && Instance._isVisible && Instance._menuRect.Contains(mouseScreenPos);
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            if (_selectionManager == null) _selectionManager = FindAnyObjectByType<TacticalSelectionManager>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();

            if (_selectionManager != null)
            {
                _selectionManager.OnOpenContextMenuRequested += OpenMenu;
                _selectionManager.OnCloseContextMenuRequested += CloseMenu;
            }
        }

        private void OnDestroy()
        {
            if (_selectionManager != null)
            {
                _selectionManager.OnOpenContextMenuRequested -= OpenMenu;
                _selectionManager.OnCloseContextMenuRequested -= CloseMenu;
            }
        }

        private void Update()
        {
            if (_isVisible && Time.frameCount > _openFrame)
            {
                // Fermer si clic en dehors du rectangle du menu
                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                {
                    Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                    if (!_menuRect.Contains(mousePos))
                    {
                        CloseMenu();
                    }
                }
            }
        }

        public void OpenMenu(TacticalUnit target, Vector2 screenPos)
        {
            if (target == null) return;

            _contextTarget = target;
            var activeUnit = GetActiveActor();

            _cachedActions = CombatActionRegistry.GetAvailableActions(activeUnit, target, _arena);

            float width = 340;
            float height = 400;

            float clampedX = Mathf.Clamp(screenPos.x, 10, Screen.width - width - 10);
            float clampedY = Mathf.Clamp(screenPos.y, 10, Screen.height - height - 10);

            _menuRect = new Rect(clampedX, clampedY, width, height);
            _isVisible = true;
            _openFrame = Time.frameCount;
        }

        public void CloseMenu()
        {
            _isVisible = false;
            _contextTarget = null;
        }

        public TacticalUnit GetActiveActor()
        {
            if (_turnManager != null && _turnManager.ActiveUnit != null) return _turnManager.ActiveUnit;
            if (_arena != null && _arena.PlayerUnit != null) return _arena.PlayerUnit;
            if (_selectionManager != null && _selectionManager.PrimarySelected != null) return _selectionManager.PrimarySelected;
            return _contextTarget;
        }

        private void OnGUI()
        {
            if (!_isVisible || _contextTarget == null) return;

            _menuRect = GUI.Window(777, _menuRect, DrawContextMenuContent, $"⚙️ Actions : {_contextTarget.Stats.Name}");
            GUI.BringWindowToFront(777);
        }

        private void DrawContextMenuContent(int windowId)
        {
            var activeActor = GetActiveActor();
            if (activeActor == null || _contextTarget == null)
            {
                GUILayout.Label("En attente d'une unité...");
                return;
            }

            // 1. En-tête de la Cible
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUI.color = _contextTarget.IsPlayerControlled ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 0.4f, 0.4f);
            GUILayout.Label($"<b>{_contextTarget.Stats.Name}</b>", GUILayout.Width(170));
            GUI.color = Color.green;
            GUILayout.Label($"❤️ {_contextTarget.Stats.CurrentHealth}/{_contextTarget.Stats.MaxHealth} PV");
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ PA Cible: {_contextTarget.Stats.CurrentActionPoints}/{_contextTarget.Stats.MaxActionPoints}", GUILayout.Width(170));
            GUI.color = Color.yellow;
            GUILayout.Label($"🛡️ Encaissement: {_contextTarget.Stats.EncaissementThreshold}");
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            if (_contextTarget.Stats.ActiveStatus != StatusEffect.None)
            {
                GUI.color = Color.magenta;
                GUILayout.Label($"⚠️ [{_contextTarget.Stats.ActiveStatus}]");
                GUI.color = Color.white;
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 2. Info de l'Acteur Actif
            GUI.color = Color.cyan;
            GUILayout.Label($"Acteur : <b>{activeActor.Stats.Name}</b> (PA Disponibles: <b>{activeActor.Stats.CurrentActionPoints}</b>)");
            GUI.color = Color.white;

            GUILayout.Space(4);

            // 3. Barre des Catégories
            GUILayout.BeginHorizontal();
            if (DrawCategoryTab("🎯 Attaque", ActionCategory.AttaqueEtPassesDarmes)) _selectedCategory = ActionCategory.AttaqueEtPassesDarmes;
            if (DrawCategoryTab("🔮 5e Force", ActionCategory.CinquiemeForceEtSorts)) _selectedCategory = ActionCategory.CinquiemeForceEtSorts;
            if (DrawCategoryTab("🩹 Soins", ActionCategory.TraumatologieEtSoins)) _selectedCategory = ActionCategory.TraumatologieEtSoins;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (DrawCategoryTab("📢 Tactique", ActionCategory.TactiqueEtOrdres)) _selectedCategory = ActionCategory.TactiqueEtOrdres;
            if (DrawCategoryTab("🛠️ Dev", ActionCategory.CommandesDev)) _selectedCategory = ActionCategory.CommandesDev;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 4. Liste des Actions de la Catégorie
            var actionsInCategory = _cachedActions.FindAll(a => a.Category == _selectedCategory);

            if (actionsInCategory.Count == 0)
            {
                GUILayout.Label("<i>Aucune action disponible dans cette catégorie.</i>", GUI.skin.box);
            }

            foreach (var action in actionsInCategory)
            {
                bool canAfford = action.CanExecute(activeActor, _contextTarget);

                GUI.backgroundColor = canAfford ? new Color(0.2f, 0.7f, 0.4f) : new Color(0.4f, 0.4f, 0.4f);
                GUI.enabled = canAfford;

                if (GUILayout.Button(action.Title, GUILayout.Height(30)))
                {
                    action.Execution?.Invoke(activeActor, _contextTarget);
                    CloseMenu();
                }

                GUI.enabled = true;
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(8);
            GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
            if (GUILayout.Button("Fermer le Menu", GUILayout.Height(24)))
            {
                CloseMenu();
            }
            GUI.backgroundColor = Color.white;
        }

        private bool DrawCategoryTab(string label, ActionCategory cat)
        {
            bool isSelected = (_selectedCategory == cat);
            GUI.backgroundColor = isSelected ? new Color(0.3f, 0.8f, 1f) : Color.white;
            bool clicked = GUILayout.Button(label, GUILayout.Height(24));
            GUI.backgroundColor = Color.white;
            return clicked;
        }
    }
}
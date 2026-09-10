using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Contrôleur central de sélection d'unités (Mono et Multi-sélection avec Shift)
    /// et déclencheur d'actions contextuelles au clic droit.
    /// </summary>
    public class TacticalSelectionManager : MonoBehaviour
    {
        public static TacticalSelectionManager Instance { get; private set; }

        [Header("Dépendances")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        private readonly List<TacticalUnit> _selectedUnits = new();
        public IReadOnlyList<TacticalUnit> SelectedUnits => _selectedUnits;
        public TacticalUnit PrimarySelected => _selectedUnits.Count > 0 ? _selectedUnits[0] : null;

        public event Action<List<TacticalUnit>> OnSelectionChanged;
        public event Action<TacticalUnit, Vector2> OnOpenContextMenuRequested;
        public event Action OnCloseContextMenuRequested;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            EnsureReferences();
        }

        private void EnsureReferences()
        {
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
        }

        private void Update()
        {
            HandleSelectionInputs();
        }

        private void HandleSelectionInputs()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null || _grid == null) return;

            Vector2 mouseScreenPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

            // Bloque les clics 3D si la souris est au-dessus du menu contextuel
            if (CombatContextMenuUI.IsPointerOverMenu(mouseScreenPos))
            {
                return;
            }

            bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // 1. CLIC GAUCHE : SÉLECTION / MULTI-SÉLECTION
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    if (_grid.TryGetNodeAtWorldPosition(hit.point, out var node))
                    {
                        var clickedUnit = GetUnitAtCoordinates(node.Coordinates);

                        if (clickedUnit != null)
                        {
                            if (isShiftHeld)
                            {
                                ToggleUnitSelection(clickedUnit);
                            }
                            else
                            {
                                SelectSingleUnit(clickedUnit);
                            }

                            if (!clickedUnit.IsPlayerControlled && _arena != null)
                            {
                                _arena.SelectTarget(clickedUnit);
                            }

                            OnCloseContextMenuRequested?.Invoke();
                            return;
                        }
                    }
                }

                if (!isShiftHeld)
                {
                    ClearSelection();
                    OnCloseContextMenuRequested?.Invoke();
                }
            }

            // 2. CLIC DROIT : OUVERTURE DU MENU CONTEXTUEL
            if (Input.GetMouseButtonDown(1))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    if (_grid.TryGetNodeAtWorldPosition(hit.point, out var node))
                    {
                        var clickedUnit = GetUnitAtCoordinates(node.Coordinates);
                        if (clickedUnit != null)
                        {
                            if (!_selectedUnits.Contains(clickedUnit))
                            {
                                SelectSingleUnit(clickedUnit);
                            }

                            OnOpenContextMenuRequested?.Invoke(clickedUnit, mouseScreenPos);
                            return;
                        }
                    }
                }
            }
        }

        public void SelectSingleUnit(TacticalUnit unit)
        {
            if (unit == null) return;

            ClearSelection();
            _selectedUnits.Add(unit);
            ApplySelectionVisuals();
            OnSelectionChanged?.Invoke(_selectedUnits);
        }

        public void ToggleUnitSelection(TacticalUnit unit)
        {
            if (unit == null) return;

            if (_selectedUnits.Contains(unit))
            {
                _selectedUnits.Remove(unit);
            }
            else
            {
                _selectedUnits.Add(unit);
            }

            ApplySelectionVisuals();
            OnSelectionChanged?.Invoke(_selectedUnits);
        }

        public void ClearSelection()
        {
            _selectedUnits.Clear();
            ApplySelectionVisuals();
            OnSelectionChanged?.Invoke(_selectedUnits);
        }

        private void ApplySelectionVisuals()
        {
            var allUnits = FindObjectsByType<TacticalUnit>();
            foreach (var u in allUnits)
            {
                var vis = u.GetComponent<TacticalUnitVisual>();
                if (vis != null)
                {
                    bool isSelected = _selectedUnits.Contains(u);
                    bool isPrimary = (PrimarySelected == u);

                    // Couleur visuelle personnalisée si sélectionné
                    if (isSelected)
                    {
                        Color selBody = isPrimary ? new Color(0.1f, 0.9f, 1f) : new Color(0.4f, 0.7f, 0.9f);
                        Color selVisor = Color.white;
                        vis.SetColor(selBody, selVisor);
                    }
                    else
                    {
                        // Rétablissement des couleurs d'équipe officielles
                        if (u.IsPlayerControlled)
                            vis.SetColor(new Color(0.15f, 0.45f, 0.85f), new Color(0.0f, 0.95f, 1.0f));
                        else
                            vis.SetColor(new Color(0.85f, 0.25f, 0.2f), new Color(1.0f, 0.75f, 0.1f));
                    }
                }
            }
        }

        public TacticalUnit GetUnitAtCoordinates(HexCoordinates coords)
        {
            var allUnits = FindObjectsByType<TacticalUnit>();
            foreach (var u in allUnits)
            {
                if (u.CurrentCoords.Equals(coords)) return u;
            }
            return null;
        }
    }
}
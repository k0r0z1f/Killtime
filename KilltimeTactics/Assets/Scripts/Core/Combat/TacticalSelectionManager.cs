using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.UI;

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
        /// <summary>
        /// Clic droit sur un objet au sol (gourdin, arme lâchée...) : ouvre le menu
        /// d'interaction objet (ramasser / kicker / détruire) au lieu du radial d'unité.
        /// </summary>
        public event Action<DroppedWeaponPickup, Vector2> OnOpenDroppedWeaponMenuRequested;

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
            if (CombatHUD.IsPaused) return;

            var cam = UnityEngine.Camera.main;
            if (cam == null || _grid == null) return;

            Vector2 mouseScreenPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

            // Bloque sélection + menu contextuel si la souris est sur une fenêtre flottante ou le HUD.
            // Sans ça, un clic dans une fenêtre sélectionne aussi une unité derrière.
            if (FloatingWindowChrome.IsPointerOverAnyWindow(mouseScreenPos))
            {
                return;
            }

            bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // 1. CLIC GAUCHE : SÉLECTION / MULTI-SÉLECTION
            if (Input.GetMouseButtonDown(0))
            {
                // Clic carte 3D : repli auto des fenêtres flottantes en coins (fantômes).
                FloatingWindowChrome.OnMapClicked();
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    // Clic gauche sur un objet au sol : on ne purge PAS la sélection
                    // (l'objet n'est pas une unité) ; on ferme juste le radial d'unité.
                    if (TryGetDroppedPickup(hit, out _))
                    {
                        OnCloseContextMenuRequested?.Invoke();
                        return;
                    }
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
                    // Priorité objet au sol : clic droit sur gourdin/arme => menu objet.
                    if (TryGetDroppedPickup(hit, out var dropped))
                    {
                        if (dropped != null && dropped.DroppedItem != null)
                            OnOpenDroppedWeaponMenuRequested?.Invoke(dropped, mouseScreenPos);
                        return;
                    }
                    if (_grid.TryGetNodeAtWorldPosition(hit.point, out var node))
                    {
                        var clickedUnit = GetUnitAtCoordinates(node.Coordinates);
                        if (clickedUnit != null)
                        {
                            // Clic droit sur un ennemi : NE PAS écraser la sélection de
                            // l'attaquant joueur. Sinon PrimarySelected devient l'ennemi
                            // et le menu calcule les PA/portée depuis la mauvaise unité
                            // (cible de derrière => "PA insuffisants" à tort).
                            if (clickedUnit.IsPlayerControlled)
                            {
                                if (!_selectedUnits.Contains(clickedUnit))
                                {
                                    SelectSingleUnit(clickedUnit);
                                }
                            }
                            else if (PrimarySelected == null || !PrimarySelected.IsPlayerControlled || !PrimarySelected.Stats.IsAlive)
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

        /// <summary>
        /// Résout le <see cref="DroppedWeaponPickup"/> visé par le raycast (clic objet).
        /// 1) hit direct sur le collider de l'arme ; 2) repli : objet au sol proche
        /// du point d'impact (≤ 0.6 m, tolérance de visée sur petit gourdin).
        /// </summary>
        private bool TryGetDroppedPickup(RaycastHit hit, out DroppedWeaponPickup pickup)
        {
            pickup = null;
            try
            {
                if (hit.transform != null)
                {
                    pickup = hit.transform.GetComponentInParent<DroppedWeaponPickup>();
                    if (pickup != null && pickup.DroppedItem != null) return true;
                }
            }
            catch { pickup = null; }
            // Repli : arme au sol juste à côté du point cliqué (petit objet, visée imprécise).
            try
            {
                var all = DroppedWeaponPickup.AllDropped;
                float best = 0.6f;
                DroppedWeaponPickup bestPick = null;
                for (int i = 0; i < all.Count; i++)
                {
                    var d = all[i];
                    if (d == null || d.DroppedItem == null) continue;
                    float dist = Vector3.Distance(d.transform.position, hit.point);
                    if (dist <= best) { best = dist; bestPick = d; }
                }
                if (bestPick != null) { pickup = bestPick; return true; }
            }
            catch { }
            pickup = null;
            return false;
        }
    }
}
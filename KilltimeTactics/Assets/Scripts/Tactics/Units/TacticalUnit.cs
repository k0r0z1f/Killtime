using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Représentation d'une unité tactique (PJ ou PNJ) sur la grille hexagonale 3D.
    /// Fait le pont entre les règles de combat (CharacterStats / CharacterSheet)
    /// et la présence physique en jeu (mouvements, visuel, grille).
    /// </summary>
    public partial class TacticalUnit : MonoBehaviour
    {
        [Header("Identité & Faction")]
        [SerializeField] private string _unitName = "Roger";
        [SerializeField] private bool _isPlayerControlled = true;
        [SerializeField] private AI.AIPersonality _aiPersonality = AI.AIPersonality.Balanced;

        public AI.AIPersonality AIPersonality
        {
            get => _aiPersonality;
            set
            {
                _aiPersonality = value;
                if (Stats != null) Stats.IsSurvivor = (_aiPersonality == AI.AIPersonality.Survivor);
            }
        }

        [Header("Attributs Physiques (Livre I)")]
        [SerializeField] private int _force = 3;
        [SerializeField] private int _agilite = 3;
        [SerializeField] private int _constitution = 4;
        [SerializeField] private int _rapidite = 3;

        [Header("Attributs Mentaux, Sociaux & Spéciaux (Livre I)")]
        [SerializeField] private int _intelligence = 2;
        [SerializeField] private int _erudition = 2;
        [SerializeField] private int _charisme = 1;
        [SerializeField] private int _instinct = 2;
        [SerializeField] private int _magie = 0;

        [Header("Combat & Armure")]
        [SerializeField] private int _baseArmor = 1;
        [SerializeField] private float _moveSpeed = 4.0f;
        [SerializeField] private string _modelPrefabName = "";
        [SerializeField] private TitanFootprintType _footprintType = TitanFootprintType.Single;

        [Header("Équipement Optique & Éclairage (RD-053)")]
        [SerializeField] private bool _hasFlashlight = false;
        [SerializeField] private bool _flashlightActive = true;

        public CharacterStats Stats { get; private set; }
        public CharacterSheet Sheet { get; private set; }
        public HexCoordinates CurrentCoords { get; private set; }
        public TitanFootprintType FootprintType => _footprintType;
        public bool IsPlayerControlled => _isPlayerControlled;
        public bool IsMoving { get; private set; }
        public bool IsStealthed => StealthState.IsStealthed(Stats);

        public bool HasFlashlight
        {
            get => (_hasFlashlight || CheckInventoryFlashlight()) && _flashlightActive;
            set
            {
                _hasFlashlight = value;
                _flashlightActive = value;
                Visibility.FogOfWarManager.Instance?.RefreshFog("flashlight");
            }
        }

        public bool FlashlightActive
        {
            get => _flashlightActive;
            set
            {
                _flashlightActive = value;
                Visibility.FogOfWarManager.Instance?.RefreshFog("flashlight");
            }
        }

        public bool HasThermalVision
        {
            get
            {
                if (Stats != null && Stats.HasSpecialization(Visibility.FogOfWarSystem.SpecThermal)) return true;
                if (SmokeScreen.HasGoggles(Stats)) return true;
                return CheckInventoryThermal();
            }
        }

        private bool CheckInventoryFlashlight()
        {
            var inv = Sheet?.Inventory ?? Stats?.Sheet?.Inventory;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (it.Name.IndexOf("Lampe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    it.Name.IndexOf("Torche", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    it.Name.IndexOf("Flashlight", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private bool CheckInventoryThermal()
        {
            var inv = Sheet?.Inventory ?? Stats?.Sheet?.Inventory;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (it.Name.IndexOf("Thermique", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    it.Name.IndexOf("Vision Nocturne", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    it.Name.IndexOf("Infrarouge", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        public void SetStealth(bool stealthed)
        {
            if (Stats != null) StealthState.SetStealth(Stats, stealthed);
        }

        /// <summary>
        /// Verrou posé par le CinematicDirector pendant qu'il pilote l'unité (trajectoire
        /// de plan cinématique) : tout déplacement en cours s'interrompt au prochain pas et
        /// aucun nouveau déplacement ne démarre tant que le verrou est posé.
        /// Sans ce verrou, MoveAlongPath continuerait d'écrire transform.position et
        /// CurrentCoords en concurrence avec la cinématique (désynchronisation visuel/logique).
        /// </summary>
        public bool CinematicDriveActive { get; set; }

        /// <summary>
        /// Interrompt un déplacement pour prise de contrôle cinématique : coupe IsMoving
        /// et libère la réservation de destination (posée en début de trajet) sauf si
        /// l'unité s'y trouve déjà ou qu'une autre unité l'occupe réellement.
        /// </summary>
        private void AbortMoveForCinematic(List<HexCoordinates> path, TacticalHexGrid grid)
        {
            IsMoving = false;
            try
            {
                if (path != null && path.Count > 0 && grid != null)
                {
                    var dest = grid.GetNode(path[^1]);
                    if (dest != null && !dest.Coordinates.Equals(CurrentCoords) && !IsAnyOtherUnitAt(path[^1]))
                        dest.IsOccupied = false;
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Vrai si l'unité a une position logique valide sur la grille.
        /// Sans position (spawn refusé + fallback épuisé), l'unité est un
        /// fantôme : invisible aux contrôles d'occupation, à détruire.
        /// </summary>
        public bool HasLogicalPosition => _hasPosition;

        public IReadOnlyList<HexCoordinates> OccupiedCoords => TitanFootprint.GetOccupiedCoordinates(CurrentCoords, _footprintType);

        public bool Occupies(HexCoordinates coords)
        {
            if (!_hasPosition) return false;
            var list = OccupiedCoords;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(coords)) return true;
            }
            return false;
        }

        public void SetFootprint(TitanFootprintType footprint)
        {
            _footprintType = footprint;
        }
        public float MoveSpeed
        {
            get
            {
                if (Stats != null)
                {
                    if (Stats.HasSpecialization("Élan Sans Drag : Évasion Inertielle"))
                        return 8.5f;
                    if (Stats.HasSpecialization("Élan Sans Drag : Friction Zéro"))
                        return 7.0f;
                    if (Stats.HasSpecialization("Élan Sans Drag : Glissade Balistique"))
                        return 6.2f;
                    if (Stats.HasSpecialization("Élan Sans Drag"))
                        return 5.5f;
                }
                return _moveSpeed;
            }
        }

        public int ComputeMovementAPCost(int rawCost)
        {
            if (rawCost <= 0) return 0;
            if (Stats != null && Stats.MovesThisTurn == 0)
            {
                if (Stats.HasSpecialization("Protocole des Pas Invisibles : Célérité Quantique"))
                {
                    return Mathf.Max(0, rawCost - 4);
                }
                if (Stats.HasSpecialization("Protocole des Pas Invisibles : Pas Fantôme"))
                {
                    return Mathf.Max(0, rawCost - 3);
                }
                if (Stats.HasSpecialization("Protocole des Pas Invisibles : Dissipation d'Échappement"))
                {
                    return Mathf.Max(0, rawCost - 2);
                }
                if (Stats.HasSpecialization("Protocole des Pas Invisibles"))
                {
                    return Mathf.Max(0, rawCost - 1);
                }
                // Course d'Endurance (Athlétisme) : après le Protocole (pas de cumul).
                if (Stats.HasSpecialization("Course d'Endurance"))
                {
                    return Mathf.Max(0, rawCost - 1);
                }
            }
            return rawCost;
        }

        public static event Action<TacticalUnit, List<HexCoordinates>, int> OnAnyUnitMoved;
        public static event Action<TacticalUnit, HexCoordinates> OnAnyUnitTeleported;
        public static event Action<TacticalUnit> OnAnyUnitMoveCompleted;

        /// <summary>
        /// Émis après chaque case franchie (RD-031) : (unité, case quittée, case
        /// d'arrivée, moveProvokes). L'arène y résout les réactions d'opportunité
        /// (frappe / poursuite / balayage) quand le contact est rompu.
        /// moveProvokes = faux si spé esquive ou Décrochage payé (aucune réaction).
        /// </summary>
        public static event Action<TacticalUnit, HexCoordinates, HexCoordinates, bool> OnAnyUnitStepCompleted;

        /// <summary>
        /// Hook de blocage (RD-031) posé par l'arène : avant chaque pas, retourne
        /// vrai pour annuler le pas (réaction Blocage réussie). Null = aucun blocage.
        /// Appelé uniquement si le déplacement provoque (ni esquive ni Décrochage).
        /// </summary>
        public static Func<TacticalUnit, HexCoordinates, HexCoordinates, bool> StepBlockCheck;

        private TacticalHexGrid _grid;
        private bool _hasPosition;

    }
}

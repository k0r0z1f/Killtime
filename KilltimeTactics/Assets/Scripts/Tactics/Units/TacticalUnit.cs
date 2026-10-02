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
    public class TacticalUnit : MonoBehaviour
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

        public bool IsCanonEntrave()
        {
            if (_grid == null) return false;
            var weapon = Sheet != null ? Sheet.GetEquippedWeapon() : null;
            bool hasLongWeapon = weapon != null && (weapon.RangeInTiles > 1
                                                    || weapon.AssociatedSkill == SkillType.Ballistique
                                                    || weapon.AssociatedSkill == SkillType.ProjectilesTir
                                                    || weapon.EquipSlot == Killtime.Core.Inventory.ItemEquipSlot.TwoHands);
            if (!hasLongWeapon) return false;

            var allUnits = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            for (int i = 0; i < allUnits.Length; i++)
            {
                var other = allUnits[i];
                if (other == null || other == this || other.Stats == null || !other.Stats.CanDefendActively()) continue;
                if (other.IsPlayerControlled != this.IsPlayerControlled)
                {
                    if (TitanFootprint.MinDistanceBetweenUnits(this.CurrentCoords, this._footprintType, other.CurrentCoords, other._footprintType) <= 1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void NotifyInventoryChanged(bool saveToDisk = true)
        {
            var visual = GetComponent<TacticalUnitVisual>();
            visual?.RefreshEquippedWeaponVisual();
            // Barrière : suit le champ porté (crédite le gain immédiat).
            if (Stats != null) Stats.RecalcShieldMax();

            if (Sheet != null && saveToDisk)
            {
                CharacterStorageService.SaveCharacter(Sheet);
            }
        }

        public void IntroduceTo(TacticalUnit other, RelationshipLinkType defaultLink = RelationshipLinkType.NeutreInconnu, string location = "Terrain Tactique", string context = "Rencontre Tactique")
        {
            if (other == null || other == this) return;
            var sheetA = GetOrBuildSheet();
            var sheetB = other.GetOrBuildSheet();
            if (sheetA == null || sheetB == null) return;
            CharacterRelationshipStorageService.IntroduceCharacters(sheetA, sheetB, defaultLink, location, context);
        }

        private void Awake()
        {
            if (Stats == null)
            {
                var attributes = new Attributes(
                    @for: _force, 
                    agi: _agilite, 
                    con: _constitution, 
                    rap: _rapidite, 
                    @int: _intelligence, 
                    eru: _erudition, 
                    cha: _charisme, 
                    ins: _instinct, 
                    mag: _magie
                );
                Stats = new CharacterStats(_unitName, attributes, _baseArmor, Sheet);
            }

            if (GetComponent<TacticalUnitVisual>() == null)
            {
                gameObject.AddComponent<TacticalUnitVisual>();
            }

            if (GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 0.9f, 0f);
                col.radius = 0.35f;
                col.height = 1.8f;
            }
        }

        private void OnDestroy()
        {
            if (_grid != null)
            {
                var list = OccupiedCoords;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    var node = _grid.GetNode(c);
                    if (node != null && !IsAnyOtherUnitAt(c))
                    {
                        node.IsOccupied = false;
                    }
                }
            }
        }

        /// <summary>
        /// Initialisation complète depuis une fiche de personnage (PJ ou PNJ).
        /// Appelée par le CharacterDevWindow lors de l'insertion sur la carte.
        /// </summary>
        public void InitializeFromSheet(CharacterSheet sheet, HexCoordinates coords, TacticalHexGrid grid, bool isPlayerControlled)
        {
            _grid = grid;
            _modelPrefabName = sheet.ModelPrefabName;
            _footprintType = sheet.Footprint;

            var effective = sheet.GetEffectiveAttributes();
            ConfigureStats(sheet.Name, effective, sheet.BaseArmor, isPlayerControlled, sheet.ModelPrefabName);

            // ConfigureStats recrée une fiche vierge : on y recopie la progression
            // de la fiche source (entraînements, spécialisations, sorts, XP).
            // Sans ça, les entraînements (ex: Mains Nues +3) et spés (ex: Arts Martiaux)
            // de l'avatar sont perdus sur la carte et ignorés en combat.
            // Note : on ne recopie NI l'espèce NI les attributs de base (effective déjà
            // appliquée ci-dessus, sinon les modificateurs raciaux seraient comptés deux fois).
            if (Sheet != null && sheet != null && !object.ReferenceEquals(Sheet, sheet))
            {
                Sheet.SheetId = sheet.SheetId;
                Sheet.Skills.Clear();
                for (int i = 0; i < sheet.Skills.Count; i++)
                {
                    var src = sheet.Skills[i];
                    if (src != null)
                    {
                        Sheet.Skills.Add(new SkillProgressionEntry(src.Skill, src.TrainingLevel));
                    }
                }

                Sheet.UnlockedSpecializations.Clear();
                if (sheet.UnlockedSpecializations != null)
                {
                    Sheet.UnlockedSpecializations.AddRange(sheet.UnlockedSpecializations);
                }

                Sheet.LearnedSpells.Clear();
                if (sheet.LearnedSpells != null)
                {
                    Sheet.LearnedSpells.AddRange(sheet.LearnedSpells);
                }

                Sheet.Inventory.Clear();
                if (sheet.Inventory != null)
                {
                    for (int i = 0; i < sheet.Inventory.Count; i++)
                    {
                        var it = sheet.Inventory[i];
                        if (it != null) Sheet.Inventory.Add(it.Clone());
                    }
                }

                Sheet.AvailableXP = sheet.AvailableXP;
                Sheet.TotalEarnedXP = sheet.TotalEarnedXP;
                Sheet.TotalSpentXP = sheet.TotalSpentXP;
                Sheet.FreeTrainingsUsed = sheet.FreeTrainingsUsed;
                Sheet.CreditsCE = sheet.CreditsCE;
                Sheet.BaseArmor = sheet.BaseArmor;
                Sheet.LoreNotes = sheet.LoreNotes;
                Sheet.Species = sheet.Species;
                Sheet.Gender = sheet.Gender;
                Sheet.Age = sheet.Age;
                Sheet.Profile = sheet.Profile;
                Sheet.ActiveClassId = sheet.ActiveClassId;
                Sheet.BuildGuideEnabled = sheet.BuildGuideEnabled;
                Sheet.Footprint = sheet.Footprint;
                if (sheet.AttributeUpgradesPurchased != null)
                {
                    Sheet.AttributeUpgradesPurchased = (int[])sheet.AttributeUpgradesPurchased.Clone();
                }
            }

            _footprintType = Sheet != null ? Sheet.Footprint : sheet.Footprint;
            if (Stats != null)
            {
                Stats.IsPlayerControlled = isPlayerControlled;
                Stats.IsSurvivor = (_aiPersonality == AI.AIPersonality.Survivor);
            }
            InitializePosition(coords, grid);
            // Filet de sécurité : si l'appelant n'a pas résolu une case libre
            // (ex: chargement direct sans arène), on relocalise au lieu d'empiler.
            if (!_hasPosition && grid != null && grid.TryFindNearestFreeCell(coords, out var sheetFallback, 8))
            {
                InitializePosition(sheetFallback, grid);
                if (_hasPosition && !sheetFallback.Equals(coords))
                {
                    Debug.Log($"[TacticalUnit] '{_unitName}' relocalisé en {sheetFallback} (demandé {coords} occupé).");
                }
            }

            var visual = GetComponent<TacticalUnitVisual>();
            if (visual != null)
            {
                if (!string.IsNullOrEmpty(sheet.ModelPrefabName))
                {
                    visual.ApplyCustomModel(sheet.ModelPrefabName);
                }
                else
                {
                    visual.BuildProceduralAvatar();
                }

                if (isPlayerControlled)
                {
                    visual.SetColor(new Color(0.15f, 0.45f, 0.85f), new Color(0.0f, 0.95f, 1.0f));
                }
                else
                {
                    visual.SetColor(new Color(0.85f, 0.25f, 0.2f), new Color(1.0f, 0.75f, 0.1f));
                }
            }
        }

        public void ConfigureStats(string unitName, Attributes attributes, int baseArmor, bool isPlayer, string modelPrefab = "")
        {
            _unitName = unitName;
            _force = attributes.Force;
            _agilite = attributes.Agilite;
            _constitution = attributes.Constitution;
            _rapidite = attributes.Rapidite;
            _intelligence = attributes.Intelligence;
            _erudition = attributes.Erudition;
            _charisme = attributes.Charisme;
            _instinct = attributes.Instinct;
            _magie = attributes.Magie;
            _baseArmor = baseArmor;
            _isPlayerControlled = isPlayer;
            if (!string.IsNullOrEmpty(modelPrefab))
            {
                _modelPrefabName = modelPrefab;
            }

            Sheet = new CharacterSheet
            {
                Name = unitName,
                BaseAttributes = attributes,
                BaseArmor = baseArmor,
                Profile = isPlayer ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                ModelPrefabName = _modelPrefabName
            };

            Stats = new CharacterStats(unitName, attributes, baseArmor, Sheet);
            Stats.IsPlayerControlled = isPlayer;
            Stats.IsSurvivor = (_aiPersonality == AI.AIPersonality.Survivor);
        }

        public CharacterSheet GetOrBuildSheet()
        {
            if (Sheet == null)
            {
                var attrs = Stats != null 
                    ? Stats.Attributes 
                    : new Attributes(_force, _agilite, _constitution, _rapidite, _intelligence, _erudition, _charisme, _instinct, _magie);

                Sheet = new CharacterSheet
                {
                    Name = _unitName,
                    BaseAttributes = attrs,
                    BaseArmor = _baseArmor,
                    Profile = _isPlayerControlled ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                    ModelPrefabName = _modelPrefabName
                };
            }
            // Maintient le lien fiche ↔ stats : la fiche affichée/éditée doit être
            // celle lue par GetSkillDie / HasSpecialization en combat.
            if (Stats != null && !object.ReferenceEquals(Stats.Sheet, Sheet))
            {
                Stats.Sheet = Sheet;
            }
            return Sheet;
        }

        public void InitializePosition(HexCoordinates startCoords, TacticalHexGrid grid)
        {
            if (grid != null && !grid.IsFootprintFree(startCoords, _footprintType, this))
            {
                Debug.LogWarning($"[TacticalUnit] Placement refusé pour '{_unitName}' en {startCoords} : empreinte encombrée.");
                return;
            }

            _grid = grid;
            if (_hasPosition && _grid != null && !CurrentCoords.Equals(startCoords))
            {
                var oldList = OccupiedCoords;
                for (int i = 0; i < oldList.Count; i++)
                {
                    var prevNode = _grid.GetNode(oldList[i]);
                    if (prevNode != null && prevNode.IsOccupied && !IsAnyOtherUnitAt(oldList[i]))
                    {
                        prevNode.IsOccupied = false;
                    }
                }
            }

            CurrentCoords = startCoords;
            var node = grid != null ? grid.GetNode(startCoords) : null;
            float yPos = node != null ? node.WorldPosition.y : 0.0f;
            transform.position = startCoords.ToWorldPosition(grid != null ? grid.HexRadius : 1.0f, yPos);

            if (_grid != null)
            {
                var newList = OccupiedCoords;
                for (int i = 0; i < newList.Count; i++)
                {
                    var n = _grid.GetNode(newList[i]);
                    if (n != null) n.IsOccupied = true;
                }
            }
            _hasPosition = true;
        }

        /// <summary>
        /// Positionne directement l'unité sans validation complexe (utile pour les tests et placements scriptés).
        /// </summary>
        public void SetPositionDirect(HexCoordinates coords, TacticalHexGrid grid = null)
        {
            CurrentCoords = coords;
            _hasPosition = true;
            if (grid != null)
            {
                _grid = grid;
                var node = grid.GetNode(coords);
                float yPos = node != null ? node.WorldPosition.y : 0.0f;
                transform.position = coords.ToWorldPosition(grid.HexRadius, yPos);
            }
            else
            {
                transform.position = coords.ToWorldPosition(1.0f, 0.0f);
            }
        }

        /// <summary>
        /// Vrai si un autre avatar occupe ces coordonnées.
        /// La présence physique d'une unité vivante fait foi et répare le nœud.
        /// </summary>
        private bool IsOccupiedByOther(HexCoordinates coords, TacticalHexGrid grid)
        {
            if (grid == null) return false;
            var node = grid.GetNode(coords);
            if (node == null || !node.IsWalkable) return true;

            if (_hasPosition && Occupies(coords)) return false;

            if (IsAnyOtherUnitAt(coords))
            {
                node.IsOccupied = true;
                return true;
            }

            return node.IsOccupied;
        }

        private bool IsAnyOtherUnitAt(HexCoordinates coords)
        {
            var all = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u == null || u == this) continue;
                if (u.gameObject == null) continue;
                if (u.Stats != null && !u.Stats.IsAlive) continue;
                if (u._hasPosition && u.Occupies(coords)) return true;
            }
            return false;
        }   

        public bool TeleportTo(HexCoordinates coords, TacticalHexGrid grid)
        {
            grid ??= _grid;
            if (grid == null) return false;

            if (_hasPosition && CurrentCoords.Equals(coords))
            {
                _grid = grid;
                return true;
            }

            if (!grid.IsFootprintFree(coords, _footprintType, this))
            {
                Debug.LogWarning($"[TacticalUnit] Téléportation refusée pour '{_unitName}' vers {coords} : empreinte occupée.");
                return false;
            }

            _grid = grid;
            if (_hasPosition)
            {
                var oldList = OccupiedCoords;
                for (int i = 0; i < oldList.Count; i++)
                {
                    var oldNode = grid.GetNode(oldList[i]);
                    if (oldNode != null && !IsAnyOtherUnitAt(oldList[i]))
                    {
                        oldNode.IsOccupied = false;
                    }
                }
            }

            InitializePosition(coords, grid);
            bool success = _hasPosition && CurrentCoords.Equals(coords);
            if (success) OnAnyUnitTeleported?.Invoke(this, coords);
            return success;
        }

        /// <summary>
        /// Déplace l'unité case par case le long d'un chemin d'hexagones en consommant ses PA.
        /// Par défaut refuse tout mouvement vers / à travers une case occupée (1 case = 1 avatar).
        /// allowPassThrough (exploration) : traverser les cases occupées pour passer
        /// de l'autre côté, sans jamais s'y arrêter (destination toujours contrôlée).
        /// provokesOpportunity (RD-031) : si vrai (défaut), quitter le contact (≤ portée)
        /// expose aux réactions d'opportunité (frappe / poursuite / blocage / balayage),
        /// sauf spé esquive (Roulade de Décrochage, Acrobatie d'Évitement) ou Décrochage
        /// payé. Les déplacements de réaction (poursuite) passent faux (pas de ping-pong).
        /// Un pas annulé par Blocage rembourse les PA non consommés (intégral si immobile).
        /// </summary>
        public IEnumerator MoveAlongPath(List<HexCoordinates> path, TacticalHexGrid grid, int apCost, bool allowPassThrough = false, bool provokesOpportunity = true)
        {
            if (path == null || path.Count <= 1) yield break;
            if (grid == null) yield break;

            // Cinématique en cours de pilotage : refuse tout nouveau déplacement
            // (avant consommation des PA et recalage).
            if (CinematicDriveActive) yield break;

            _grid = grid;

            // En cas de léger décalage réseau ou rejeu, recalage immédiat sur le départ du chemin
            if (!path[0].Equals(CurrentCoords))
            {
                TeleportTo(path[0], grid);
            }

            // Validation préalable : praticable partout ; occupée = refus sauf
            // faufile (traversée autorisée, destination toujours contrôlée).
            for (int v = 1; v < path.Count; v++)
            {
                bool isDest = v == path.Count - 1;
                if ((!allowPassThrough || isDest) && IsOccupiedByOther(path[v], grid))
                {
                    Debug.LogWarning($"[TacticalUnit] Déplacement refusé pour '{_unitName}' : {path[v]} occupée.");
                    yield break;
                }
                var vNode = grid.GetNode(path[v]);
                if (vNode == null || !vNode.IsWalkable)
                {
                    Debug.LogWarning($"[TacticalUnit] Déplacement refusé pour '{_unitName}' : {path[v]} impraticable.");
                    yield break;
                }
            }

            var destNode = grid.GetNode(path[^1]);
            if (destNode != null)
            {
                destNode.IsOccupied = true;
            }

            IsMoving = true;
            Stats.ConsumeActionPoints(apCost);
            Stats.RegisterMove();
            OnAnyUnitMoved?.Invoke(this, path, apCost);

            // RD-031 : ce déplacement expose-t-il aux réactions d'opportunité ?
            // Faux si spé esquive (Roulade de Décrochage, Acrobatie d'Évitement)
            // ou Décrochage payé (consommé ici, valable pour tout le trajet).
            bool moveProvokes = provokesOpportunity
                && (Stats == null || OpportunityState.TryBeginProvokingMove(Stats));

            HexCoordinates previous = path[0];

            for (int i = 1; i < path.Count; i++)
            {
                // Prise de contrôle cinématique : interrompt le déplacement en cours
                // (la cinématique re-synchronise occupancy via TeleportTo).
                if (CinematicDriveActive)
                {
                    AbortMoveForCinematic(path, grid);
                    yield break;
                }

                var nextCoords = path[i];

                // RD-031 Blocage : un voisin en posture Blocage peut annuler le pas
                // (duel opposé résolu par l'arène). Les PA non consommés sont
                // remboursés — intégralement si l'unité n'a pas bougé.
                if (moveProvokes && IsStepBlockedByReaction(previous, nextCoords))
                {
                    Debug.LogWarning($"[TacticalUnit] Déplacement bloqué pour '{_unitName}' : réaction d'opportunité (pas {previous} -> {nextCoords} annulé).");
                    RefundUnspentMovePA(apCost, path.Count - 1, i - 1);
                    if (Stats != null && i - 1 > 0)
                    {
                        bool charged = ChargeState.ApplyMovement(Stats, i - 1);
                        if (charged)
                        {
                            GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⚡ CHARGE / SPRINT", new Color(1f, 0.85f, 0.2f));
                        }
                    }
                    var blockedFallback = grid.GetNode(CurrentCoords);
                    if (blockedFallback != null) blockedFallback.IsOccupied = true;
                    if (destNode != null && !destNode.Coordinates.Equals(CurrentCoords)) destNode.IsOccupied = false;
                    IsMoving = false;
                    yield break;
                }

                // La destination est exclue du test drapeau (réservée en début
                // de trajet) mais pas du test physique : si un autre avatar y
                // est arrivé entre-temps, on s'arrête AVANT d'entrer (pas d'empilement).
                // En faufile on traverse les étapes occupées, jamais la destination.
                bool isDestStep = nextCoords.Equals(path[^1]);
                bool stepBlocked = isDestStep
                    ? IsAnyOtherUnitAt(nextCoords)
                    : (!allowPassThrough && IsOccupiedByOther(nextCoords, grid));
                if (stepBlocked)
                {
                    Debug.LogWarning($"[TacticalUnit] Déplacement interrompu pour '{_unitName}' : {nextCoords} devenue occupée.");
                    var fallbackNode = grid.GetNode(CurrentCoords);
                    if (fallbackNode != null) fallbackNode.IsOccupied = true;
                    if (destNode != null && !destNode.Coordinates.Equals(CurrentCoords)) destNode.IsOccupied = false;
                    IsMoving = false;
                    yield break;
                }

                var nextNode = grid.GetNode(nextCoords);
                if (nextNode == null || !nextNode.IsWalkable)
                {
                    var fallbackNode2 = grid.GetNode(CurrentCoords);
                    if (fallbackNode2 != null) fallbackNode2.IsOccupied = true;
                    IsMoving = false;
                    yield break;
                }

                float yPos = nextNode.WorldPosition.y;
                var targetPos = nextCoords.ToWorldPosition(grid.HexRadius, yPos);

                // Rotation orientée vers la destination
                Vector3 lookDir = (targetPos - transform.position).normalized;
                if (lookDir != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir);
                }

                // Déplacement progressif
                while (Vector3.Distance(transform.position, targetPos) > 0.05f)
                {
                    // Prise de contrôle cinématique même au milieu d'un pas.
                    if (CinematicDriveActive)
                    {
                        AbortMoveForCinematic(path, grid);
                        yield break;
                    }

                    while (Time.timeScale <= 0.0001f)
                    {
                        yield return null;
                    }

                    transform.position = Vector3.MoveTowards(transform.position, targetPos, _moveSpeed * Time.deltaTime);
                    yield return null;
                }

                transform.position = targetPos;
                // L'occupation suit l'unité : libère la précédente, occupe la nouvelle.
                var prevNode = grid.GetNode(previous);
                if (prevNode != null && !IsAnyOtherUnitAt(previous))
                {
                    prevNode.IsOccupied = false;
                }
                CurrentCoords = nextCoords;
                nextNode.IsOccupied = true;
                previous = nextCoords;
                _hasPosition = true;
                // SFX : pas spatialisé à chaque case (cooldown interne anti-spam).
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Footstep, transform.position, 0.8f);

                // RD-031 : notifie la case franchie pour les réactions d'opportunité
                // (frappe / poursuite / balayage si le contact est rompu).
                if (moveProvokes)
                {
                    try { OnAnyUnitStepCompleted?.Invoke(this, path[i - 1], nextCoords, moveProvokes); }
                    catch { /* handler arène optionnel */ }
                }

                // Tué en cours de trajet (frappe d'opportunité) : le reste du
                // chemin est annulé, l'unité reste sur la case atteinte.
                if (Stats != null && !Stats.IsAlive)
                {
                    IsMoving = false;
                    yield break;
                }
            }

            IsMoving = false;
            int totalSteps = path.Count - 1;
            if (Stats != null && totalSteps > 0)
            {
                bool charged = ChargeState.ApplyMovement(Stats, totalSteps);
                if (charged)
                {
                    GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⚡ CHARGE / SPRINT", new Color(1f, 0.85f, 0.2f));
                }
            }
            try { OnAnyUnitMoveCompleted?.Invoke(this); } catch { /* ignore */ }
        }

        /// <summary>
        /// Interroge le hook de blocage posé par l'arène (RD-031 Blocage).
        /// Toute exception du handler vaut "pas autorisé" (jamais de blocage fantôme).
        /// </summary>
        private bool IsStepBlockedByReaction(HexCoordinates from, HexCoordinates to)
        {
            var check = StepBlockCheck;
            if (check == null) return false;
            try { return check(this, from, to); }
            catch { return false; }
        }

        /// <summary>
        /// Rembourse les PA du trajet non effectué après un Blocage (RD-031) :
        /// intégral si aucun pas accompli, prorata arrondi au supérieur sinon.
        /// Plafonné au maximum (jamais de PA au-delà du plafond).
        /// </summary>
        private void RefundUnspentMovePA(int apCost, int totalSteps, int completedSteps)
        {
            if (Stats == null || apCost <= 0 || totalSteps <= 0) return;
            int refund = apCost;
            if (completedSteps > 0)
            {
                int remaining = totalSteps - completedSteps;
                if (remaining <= 0) return;
                refund = Mathf.CeilToInt((float)apCost * remaining / totalSteps);
            }
            refund = Mathf.Clamp(refund, 0, apCost);
            if (refund > 0)
                Stats.CurrentActionPoints = Mathf.Min(Stats.MaxActionPoints, Stats.CurrentActionPoints + refund);
        }
    }
}
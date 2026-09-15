using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Core.Character;
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

        public CharacterStats Stats { get; private set; }
        public CharacterSheet Sheet { get; private set; }
        public HexCoordinates CurrentCoords { get; private set; }
        public bool IsPlayerControlled => _isPlayerControlled;
        public bool IsMoving { get; private set; }
        public float MoveSpeed => _moveSpeed;

        private TacticalHexGrid _grid;
        private bool _hasPosition;

        public bool IsCanonEntrave()
        {
            if (_grid == null) return false;
            var weapon = Sheet != null ? Sheet.GetEquippedWeapon() : null;
            bool hasLongWeapon = (weapon != null && (weapon.RangeInTiles > 1
                                                     || weapon.AssociatedSkill == SkillType.Ballistique
                                                     || weapon.AssociatedSkill == SkillType.ProjectilesTir
                                                     || weapon.EquipSlot == Killtime.Core.Inventory.ItemEquipSlot.TwoHands))
                                 || (GetComponent<TacticalUnitVisual>()?.HasRifleEquipped() == true);
            if (!hasLongWeapon) return false;

            var allUnits = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            for (int i = 0; i < allUnits.Length; i++)
            {
                var other = allUnits[i];
                if (other == null || other == this || other.Stats == null || !other.Stats.CanDefendActively()) continue;
                if (other.IsPlayerControlled != this.IsPlayerControlled)
                {
                    // N'importe quel ennemi conscient au contact de l'ATTAQUANT suffit,
                    // même si la cible du tir est lointaine (Livre VI §26.2).
                    if (this.CurrentCoords.DistanceTo(other.CurrentCoords) == 1)
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

            if (Sheet != null && saveToDisk)
            {
                CharacterStorageService.SaveCharacter(Sheet);
            }
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
        }

        private void OnDestroy()
        {
            if (_grid != null)
            {
                var node = _grid.GetNode(CurrentCoords);
                if (node != null && !IsAnyOtherUnitAt(CurrentCoords))
                {
                    node.IsOccupied = false;
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
                Sheet.CreditsCE = sheet.CreditsCE;
                Sheet.BaseArmor = sheet.BaseArmor;
                Sheet.LoreNotes = sheet.LoreNotes;
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
            // Invariant : 1 case = 1 avatar max. Refuse toute superposition.
            if (grid != null && IsOccupiedByOther(startCoords, grid))
            {
                Debug.LogWarning($"[TacticalUnit] Placement refusé pour '{_unitName}' en {startCoords} : case déjà occupée.");
                return;
            }

            _grid = grid;
            // Libère l'ancienne case si on repositionne une unité déjà placée.
            if (_hasPosition && _grid != null && !CurrentCoords.Equals(startCoords))
            {
                var previousNode = _grid.GetNode(CurrentCoords);
                // Ne libère que si aucun autre avatar n'y réside (anti-fantôme).
                if (previousNode != null && previousNode.IsOccupied && !IsAnyOtherUnitAt(CurrentCoords))
                {
                    previousNode.IsOccupied = false;
                }
            }

            CurrentCoords = startCoords;
            var node = grid != null ? grid.GetNode(startCoords) : null;
            float yPos = node != null ? node.WorldPosition.y : 0.0f;
            transform.position = startCoords.ToWorldPosition(grid != null ? grid.HexRadius : 1.0f, yPos);
            
            if (node != null)
            {
                node.IsOccupied = true;
            }
            _hasPosition = true;
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

            if (_hasPosition && CurrentCoords.Equals(coords)) return false;

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
                if (u._hasPosition && u.CurrentCoords.Equals(coords)) return true;
            }
            return false;
        }

        public bool TeleportTo(HexCoordinates coords, TacticalHexGrid grid)
        {
            grid ??= _grid;
            if (grid == null) return false;

            // Rester sur place : toujours autorisé.
            if (_hasPosition && CurrentCoords.Equals(coords))
            {
                _grid = grid;
                return true;
            }

            if (IsOccupiedByOther(coords, grid))
            {
                Debug.LogWarning($"[TacticalUnit] Téléportation refusée pour '{_unitName}' vers {coords} : case déjà occupée.");
                return false;
            }

            _grid = grid;
            var oldNode = _hasPosition ? grid.GetNode(CurrentCoords) : null;
            if (oldNode != null && !IsAnyOtherUnitAt(CurrentCoords))
            {
                oldNode.IsOccupied = false;
            }

            InitializePosition(coords, grid);
            return _hasPosition && CurrentCoords.Equals(coords);
        }

        /// <summary>
        /// Déplace l'unité case par case le long d'un chemin d'hexagones en consommant ses PA.
        /// Refuse tout mouvement vers / à travers une case occupée (1 case = 1 avatar).
        /// </summary>
        public IEnumerator MoveAlongPath(List<HexCoordinates> path, TacticalHexGrid grid, int apCost)
        {
            if (path == null || path.Count <= 1) yield break;
            if (grid == null) yield break;

            _grid = grid;

            // Le chemin doit partir de notre position réelle.
            if (!path[0].Equals(CurrentCoords))
            {
                Debug.LogWarning($"[TacticalUnit] Déplacement refusé pour '{_unitName}' : départ {path[0]} != position {CurrentCoords}.");
                yield break;
            }

            // Validation préalable : aucune étape (sauf départ) ne doit être occupée.
            for (int v = 1; v < path.Count; v++)
            {
                if (IsOccupiedByOther(path[v], grid))
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

            HexCoordinates previous = path[0];

            for (int i = 1; i < path.Count; i++)
            {
                var nextCoords = path[i];

                if (IsOccupiedByOther(nextCoords, grid) && !nextCoords.Equals(path[^1]))
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
            }

            IsMoving = false;
        }
    }
}
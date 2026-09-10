using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

        private TacticalHexGrid _grid;

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
                Stats = new CharacterStats(_unitName, attributes, _baseArmor);
            }

            if (GetComponent<TacticalUnitVisual>() == null)
            {
                gameObject.AddComponent<TacticalUnitVisual>();
            }
        }

        private void OnDestroy()
        {
            // Libère la case de la grille si l'unité est détruite (évite les cases fantômes bloquées)
            if (_grid != null)
            {
                var node = _grid.GetNode(CurrentCoords);
                if (node != null)
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
            Sheet = sheet;
            _grid = grid;
            _modelPrefabName = sheet.ModelPrefabName;

            var effective = sheet.GetEffectiveAttributes();
            ConfigureStats(sheet.Name, effective, sheet.BaseArmor, isPlayerControlled, sheet.ModelPrefabName);
            InitializePosition(coords, grid);

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

            Stats = new CharacterStats(unitName, attributes, baseArmor);

            Sheet = new CharacterSheet
            {
                Name = unitName,
                BaseAttributes = attributes,
                BaseArmor = baseArmor,
                Profile = isPlayer ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                ModelPrefabName = _modelPrefabName
            };
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
            return Sheet;
        }

        public void InitializePosition(HexCoordinates startCoords, TacticalHexGrid grid)
        {
            _grid = grid;
            CurrentCoords = startCoords;
            transform.position = startCoords.ToWorldPosition(grid.HexRadius, 0.0f);
            
            var node = grid.GetNode(startCoords);
            if (node != null)
            {
                node.IsOccupied = true;
            }
        }

        public void TeleportTo(HexCoordinates coords, TacticalHexGrid grid)
        {
            _grid = grid;
            var oldNode = grid.GetNode(CurrentCoords);
            if (oldNode != null)
            {
                oldNode.IsOccupied = false;
            }

            InitializePosition(coords, grid);
        }

        /// <summary>
        /// Déplace l'unité case par case le long d'un chemin d'hexagones en consommant ses PA.
        /// </summary>
        public IEnumerator MoveAlongPath(List<HexCoordinates> path, TacticalHexGrid grid, int apCost)
        {
            if (path == null || path.Count <= 1) yield break;

            _grid = grid;
            IsMoving = true;
            Stats.ConsumeActionPoints(apCost);

            // Libérer la case de départ
            var oldNode = grid.GetNode(CurrentCoords);
            if (oldNode != null)
            {
                oldNode.IsOccupied = false;
            }

            for (int i = 1; i < path.Count; i++)
            {
                var nextCoords = path[i];
                var targetPos = nextCoords.ToWorldPosition(grid.HexRadius, 0.0f);

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
                CurrentCoords = nextCoords;
            }

            // Marquer la case d'arrivée comme occupée
            var newNode = grid.GetNode(CurrentCoords);
            if (newNode != null)
            {
                newNode.IsOccupied = true;
            }

            IsMoving = false;
        }
    }
}
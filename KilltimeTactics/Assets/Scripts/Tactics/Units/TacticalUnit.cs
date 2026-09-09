using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Représentation d'une unité tactique (PJ ou PNJ) sur la grille 3D.
    /// </summary>
    public class TacticalUnit : MonoBehaviour
    {
        [Header("Identité & Faction")]
        [SerializeField] private string _unitName = "Roger";
        [SerializeField] private bool _isPlayerControlled = true;

        [Header("Attributs Fondamentaux (Livre I)")]
        [SerializeField] private int _agilite = 3;
        [SerializeField] private int _intelligence = 2;
        [SerializeField] private int _rapidite = 3;
        [SerializeField] private int _constitution = 4;
        [SerializeField] private int _force = 3;
        [SerializeField] private int _charisme = 1;

        [Header("Combat & Armure")]
        [SerializeField] private int _baseArmor = 1;
        [SerializeField] private float _moveSpeed = 4.0f;

        public CharacterStats Stats { get; private set; }
        public HexCoordinates CurrentCoords { get; private set; }
        public bool IsPlayerControlled => _isPlayerControlled;
        public bool IsMoving { get; private set; }

        private void Awake()
        {
            var attributes = new Attributes(_agilite, _intelligence, _rapidite, _constitution, _force, _charisme);
            Stats = new CharacterStats(_unitName, attributes, _baseArmor);
        }

        public void InitializePosition(HexCoordinates startCoords, TacticalHexGrid grid)
        {
            CurrentCoords = startCoords;
            transform.position = startCoords.ToWorldPosition(grid.HexRadius, 0.0f);
            
            var node = grid.GetNode(startCoords);
            if (node != null) node.IsOccupied = true;
        }

        /// <summary>
        /// Déplace l'unité le long d'un chemin d'hexagones en consommant des PA.
        /// </summary>
        public IEnumerator MoveAlongPath(List<HexCoordinates> path, TacticalHexGrid grid, int apCost)
        {
            if (path == null || path.Count <= 1) yield break;

            IsMoving = true;
            Stats.ConsumeActionPoints(apCost);

            // Libérer l'ancien nœud
            var oldNode = grid.GetNode(CurrentCoords);
            if (oldNode != null) oldNode.IsOccupied = false;

            for (int i = 1; i < path.Count; i++)
            {
                var nextCoords = path[i];
                var targetPos = nextCoords.ToWorldPosition(grid.HexRadius, 0.0f);

                // Rotation vers la cible
                Vector3 lookDir = (targetPos - transform.position).normalized;
                if (lookDir != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir);
                }

                // Interpolation de déplacement
                while (Vector3.Distance(transform.position, targetPos) > 0.05f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, targetPos, _moveSpeed * Time.deltaTime);
                    yield return null;
                }

                transform.position = targetPos;
                CurrentCoords = nextCoords;
            }

            // Occuper le nouveau nœud
            var newNode = grid.GetNode(CurrentCoords);
            if (newNode != null) newNode.IsOccupied = true;

            IsMoving = false;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Tactics.Grid
{
    /// <summary>
    /// Obstacle 3D posé sur une case (pilier, caisse, mobilier, mur prop).
    /// Dimensions RÉELLES (boîtes englobantes monde) : un pilier bloque même si
    /// la case est marquée None, une caisse basse ne masque que le bas du corps.
    /// MeshBounds = vérité affichée (prioritaire) ; sinon repli prisme hexagonal.
    /// </summary>
    public struct PropObstacle
    {
        public Vector2 CenterXZ;
        public float Radius;
        public float MinY;
        public float MaxY;
        public Bounds MeshBounds;
        public bool HasMeshBounds;
        public string PropName;
    }

    /// <summary>
    /// Registre des accessoires 3D faisant obstacle à la ligne de mire
    /// (Livre VI §25.3) : piliers, caisses, mobilier, murs props. Reconstruit au
    /// chargement de carte et à chaque pose/retrait dans l'éditeur ; consulté par
    /// CoverSystem.RayBlocked en plus du marquage node.Cover et du relief.
    /// Les accessoires suspendus (plafond, flottants) passent AU-DESSUS des rayons
    /// émergemment via leurs bornes : aucun filtrage par type nécessaire.
    /// </summary>
    public static class PropObstacleRegistry
    {
        private static readonly Dictionary<HexCoordinates, PropObstacle> _obstacles = new();

        public static int Count => _obstacles.Count;

        public static void Clear() => _obstacles.Clear();

        public static bool TryGet(HexCoordinates coords, out PropObstacle obstacle)
            => _obstacles.TryGetValue(coords, out obstacle);

        /// <summary>Injection directe (tests, sans scène).</summary>
        public static void SetForTests(HexCoordinates coords, PropObstacle obstacle)
            => _obstacles[coords] = obstacle;

        /// <summary>
        /// Reconstruit le registre depuis les MapPropInstance de la scène.
        /// Hauteur = bornes monde réelles (× échelle incluse) ; empreinte = max
        /// (largeur, profondeur) / 2, plafonnée au rayon de l'hexagone.
        /// Sans MeshRenderer : repli sur la hauteur du marquage Cover du prop.
        /// </summary>
        public static void Rebuild(float hexRadius = 1f)
        {
            _obstacles.Clear();
            MapPropInstance[] instances;
            try { instances = Object.FindObjectsByType<MapPropInstance>(); }
            catch { return; }
            if (instances == null) return;

            for (int i = 0; i < instances.Length; i++)
            {
                var inst = instances[i];
                if (inst == null || inst.gameObject == null || !inst.gameObject.activeInHierarchy) continue;
                var data = inst.Data;
                if (data == null) continue;

                var coords = new HexCoordinates(data.Q, data.R);
                var renderers = inst.GetComponentsInChildren<MeshRenderer>(true);
                SkinnedMeshRenderer[] skinned = null;
                try { skinned = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true); }
                catch { /* ignore */ }

                bool hasBounds = false;
                var bounds = new Bounds(inst.transform.position, Vector3.zero);
                if (renderers != null)
                {
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        if (renderers[r] == null) continue;
                        if (!hasBounds) { bounds = renderers[r].bounds; hasBounds = true; }
                        else bounds.Encapsulate(renderers[r].bounds);
                    }
                }
                if (skinned != null)
                {
                    for (int r = 0; r < skinned.Length; r++)
                    {
                        if (skinned[r] == null) continue;
                        if (!hasBounds) { bounds = skinned[r].bounds; hasBounds = true; }
                        else bounds.Encapsulate(skinned[r].bounds);
                    }
                }

                PropObstacle obstacle;
                if (hasBounds && bounds.size.y > 0.01f)
                {
                    float radius = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f, 0.05f, hexRadius);
                    obstacle = new PropObstacle
                    {
                        CenterXZ = new Vector2(bounds.center.x, bounds.center.z),
                        Radius = radius,
                        MinY = bounds.min.y,
                        MaxY = bounds.max.y,
                        MeshBounds = bounds,
                        HasMeshBounds = true,
                        PropName = data.PrefabName ?? "Prop"
                    };
                }
                else
                {
                    // Repli : hauteur du marquage Cover, empreinte case entière.
                    float h = data.Cover switch
                    {
                        CoverType.Half => 0.7f,
                        CoverType.ThreeQuarters => 1.2f,
                        CoverType.Full => 2.2f,
                        _ => 0f,
                    };
                    if (h <= 0f) continue;
                    Vector3 anchor = inst.transform.position;
                    obstacle = new PropObstacle
                    {
                        CenterXZ = new Vector2(anchor.x, anchor.z),
                        Radius = hexRadius,
                        MinY = anchor.y,
                        MaxY = anchor.y + h,
                        MeshBounds = new Bounds(),
                        HasMeshBounds = false,
                        PropName = data.PrefabName ?? "Prop"
                    };
                }

                _obstacles[coords] = obstacle;
            }

            // Visuels d'obstacles de la grille (murets, barricades, murs) : leurs
            // bornes affichées priment — tout rayon traversant le mesh rougit.
            try
            {
                var visualizer = Object.FindAnyObjectByType<HexGridVisualizer>();
                if (visualizer != null)
                {
                    var meshBounds = visualizer.GetObstacleMeshBounds();
                    for (int i = 0; i < meshBounds.Count; i++)
                    {
                        Bounds b = meshBounds[i].Value;
                        _obstacles[meshBounds[i].Key] = new PropObstacle
                        {
                            CenterXZ = new Vector2(b.center.x, b.center.z),
                            Radius = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.5f, 0.05f, hexRadius),
                            MinY = b.min.y,
                            MaxY = b.max.y,
                            MeshBounds = b,
                            HasMeshBounds = true,
                            PropName = "CouvertGrille"
                        };
                    }
                }
            }
            catch { /* ignore */ }
        }
    }
}

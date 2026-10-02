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
        public ObstacleMaterial Material;
        public int CurrentHealth;
        public int MaxHealth;
        public int Hardness;
        public CoverType InitialCover;
        public CoverType CurrentCover;
        public bool IsIndestructible;
        public GameObject SceneInstance;
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

                var mat = DestructibleEnvironmentRules.DeduceMaterialFromName(data.PrefabName, data.Cover);
                var profile = DestructibleEnvironmentRules.GetProfile(mat);
                bool isIndestructible = mat == ObstacleMaterial.Indestructible;

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
                        PropName = data.PrefabName ?? "Prop",
                        Material = mat,
                        CurrentHealth = profile.BaseHP,
                        MaxHealth = profile.BaseHP,
                        Hardness = profile.Hardness,
                        InitialCover = data.Cover,
                        CurrentCover = data.Cover,
                        IsIndestructible = isIndestructible,
                        SceneInstance = inst.gameObject
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
                        PropName = data.PrefabName ?? "Prop",
                        Material = mat,
                        CurrentHealth = profile.BaseHP,
                        MaxHealth = profile.BaseHP,
                        Hardness = profile.Hardness,
                        InitialCover = data.Cover,
                        CurrentCover = data.Cover,
                        IsIndestructible = isIndestructible,
                        SceneInstance = inst.gameObject
                    };
                }

                _obstacles[coords] = obstacle;
            }

            // Visuels d'obstacles de la grille (murets, barricades, murs) : leurs
            // bornes affichées priment — tout rayon traversant le mesh rougit.
            try
            {
                var visualizer = Object.FindAnyObjectByType<HexGridVisualizer>();
                var grid = Object.FindAnyObjectByType<TacticalHexGrid>();
                if (visualizer != null)
                {
                    var meshBounds = visualizer.GetObstacleMeshBounds();
                    for (int i = 0; i < meshBounds.Count; i++)
                    {
                        var cellCoords = meshBounds[i].Key;
                        if (_obstacles.ContainsKey(cellCoords)) continue;

                        Bounds b = meshBounds[i].Value;
                        CoverType cellCover = CoverType.Half;
                        if (grid != null)
                        {
                            var n = grid.GetNode(cellCoords);
                            if (n != null) cellCover = n.Cover;
                        }

                        var mat = cellCover == CoverType.Full ? ObstacleMaterial.Pierre : ObstacleMaterial.Bois;
                        var profile = DestructibleEnvironmentRules.GetProfile(mat);

                        _obstacles[cellCoords] = new PropObstacle
                        {
                            CenterXZ = new Vector2(b.center.x, b.center.z),
                            Radius = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.5f, 0.05f, hexRadius),
                            MinY = b.min.y,
                            MaxY = b.max.y,
                            MeshBounds = b,
                            HasMeshBounds = true,
                            PropName = cellCover == CoverType.Full ? "MurGrille" : "MuretGrille",
                            Material = mat,
                            CurrentHealth = profile.BaseHP,
                            MaxHealth = profile.BaseHP,
                            Hardness = profile.Hardness,
                            InitialCover = cellCover,
                            CurrentCover = cellCover,
                            IsIndestructible = false,
                            SceneInstance = null
                        };
                    }
                }
            }
            catch { /* ignore */ }
        }

        public static bool ApplyDamage(
            HexCoordinates coords,
            int rawDamage,
            ObstacleDamageType damageType,
            TacticalHexGrid grid,
            HexGridVisualizer visualizer,
            out ObstacleDamageResult result)
        {
            result = default;
            if (rawDamage <= 0) return false;

            HexNode node = grid != null ? grid.GetNode(coords) : null;
            bool hasRegisteredObstacle = _obstacles.TryGetValue(coords, out PropObstacle prop);

            // Gestion de l'effondrement de plafond si la case n'a pas d'obstacle au sol mais un plafond
            if (!hasRegisteredObstacle && node != null && node.HasCeiling && damageType == ObstacleDamageType.Explosif)
            {
                var ceilingMat = ObstacleMaterial.PlafondPlatre;
                var ceilingProf = DestructibleEnvironmentRules.GetProfile(ceilingMat);
                float ceilingMult = DestructibleEnvironmentRules.ComputeDamageMultiplier(ceilingMat, damageType);
                int scaledCeil = Mathf.RoundToInt(rawDamage * ceilingMult);
                int soakedCeil = Mathf.Min(scaledCeil, ceilingProf.Hardness);
                int netCeil = Mathf.Max(0, scaledCeil - soakedCeil);

                if (netCeil >= ceilingProf.BaseHP)
                {
                    node.HasCeiling = false;
                    node.Cover = CoverType.Half;
                    node.IsWalkable = true;
                    visualizer?.RefreshObstacles();

                    result = new ObstacleDamageResult
                    {
                        Coordinates = coords,
                        ObstacleName = "Plafond",
                        Material = ceilingMat,
                        RawDamage = rawDamage,
                        HardnessSoaked = soakedCeil,
                        AppliedDamage = netCeil,
                        PreviousHP = ceilingProf.BaseHP,
                        CurrentHP = 0,
                        PreviousCover = CoverType.None,
                        CurrentCover = CoverType.Half,
                        TransitionedToHalf = true,
                        Destroyed = true,
                        CeilingCollapsed = true,
                        Log = $"💥 <b>EFFONDREMENT DU PLAFOND</b> en ({coords.Q}, {coords.R}) ! Les décombres créent un demi-couvert au sol."
                    };
                    return true;
                }
                return false;
            }

            if (!hasRegisteredObstacle)
            {
                if (node == null || node.Cover == CoverType.None) return false;

                var fallbackMat = node.Cover == CoverType.Full ? ObstacleMaterial.Pierre : ObstacleMaterial.Bois;
                var fallbackProf = DestructibleEnvironmentRules.GetProfile(fallbackMat);
                prop = new PropObstacle
                {
                    CenterXZ = new Vector2(node.WorldPosition.x, node.WorldPosition.z),
                    Radius = grid != null ? grid.HexRadius : 1f,
                    MinY = node.WorldPosition.y,
                    MaxY = node.WorldPosition.y + 1f,
                    PropName = "ObstacleProcedural",
                    Material = fallbackMat,
                    CurrentHealth = fallbackProf.BaseHP,
                    MaxHealth = fallbackProf.BaseHP,
                    Hardness = fallbackProf.Hardness,
                    InitialCover = node.Cover,
                    CurrentCover = node.Cover,
                    IsIndestructible = false,
                    SceneInstance = null
                };
            }

            if (prop.IsIndestructible)
            {
                result = new ObstacleDamageResult
                {
                    Coordinates = coords,
                    ObstacleName = prop.PropName,
                    Material = prop.Material,
                    RawDamage = rawDamage,
                    HardnessSoaked = rawDamage,
                    AppliedDamage = 0,
                    PreviousHP = prop.CurrentHealth,
                    CurrentHP = prop.CurrentHealth,
                    PreviousCover = prop.CurrentCover,
                    CurrentCover = prop.CurrentCover,
                    TransitionedToHalf = false,
                    Destroyed = false,
                    CeilingCollapsed = false,
                    Log = $"🛡️ <b>{prop.PropName}</b> ({prop.Material}) est <b>INDESTRUCTIBLE</b> — aucun dommage structurel subi."
                };
                return false;
            }

            float mult = DestructibleEnvironmentRules.ComputeDamageMultiplier(prop.Material, damageType);
            int scaledDmg = Mathf.RoundToInt(rawDamage * mult);
            int soaked = Mathf.Min(scaledDmg, prop.Hardness);
            int netDmg = Mathf.Max(0, scaledDmg - soaked);

            int prevHP = prop.CurrentHealth;
            int newHP = Mathf.Max(0, prevHP - netDmg);
            CoverType prevCover = prop.CurrentCover;
            CoverType newCover = prevCover;

            bool transitioned = false;
            bool destroyed = false;

            int halfThreshold = Mathf.RoundToInt(prop.MaxHealth * 0.45f);

            if (newHP <= 0)
            {
                destroyed = true;
                newCover = CoverType.None;
                _obstacles.Remove(coords);

                if (prop.SceneInstance != null)
                {
                    prop.SceneInstance.SetActive(false);
                    var instComp = prop.SceneInstance.GetComponent<MapPropInstance>();
                    if (instComp != null && instComp.Data != null) instComp.Data.Cover = CoverType.None;
                }

                if (node != null)
                {
                    node.Cover = CoverType.None;
                    node.IsWalkable = true;
                    node.HasCustomVisual = false;
                }
            }
            else if (newHP <= halfThreshold && (prop.CurrentCover == CoverType.Full || prop.CurrentCover == CoverType.ThreeQuarters))
            {
                transitioned = true;
                newCover = CoverType.Half;
                prop.CurrentCover = CoverType.Half;
                prop.CurrentHealth = newHP;

                if (prop.HasMeshBounds)
                {
                    float newHeight = (prop.MeshBounds.max.y - prop.MeshBounds.min.y) * 0.5f;
                    prop.MeshBounds = new Bounds(
                        new Vector3(prop.MeshBounds.center.x, prop.MeshBounds.min.y + newHeight * 0.5f, prop.MeshBounds.center.z),
                        new Vector3(prop.MeshBounds.size.x, newHeight, prop.MeshBounds.size.z));
                }
                prop.MaxY = prop.MinY + (prop.MaxY - prop.MinY) * 0.5f;

                if (prop.SceneInstance != null)
                {
                    var curScale = prop.SceneInstance.transform.localScale;
                    prop.SceneInstance.transform.localScale = new Vector3(curScale.x, curScale.y * 0.5f, curScale.z);
                    var instComp = prop.SceneInstance.GetComponent<MapPropInstance>();
                    if (instComp != null && instComp.Data != null) instComp.Data.Cover = CoverType.Half;
                }

                if (node != null)
                {
                    node.Cover = CoverType.Half;
                    node.IsWalkable = true;
                }

                _obstacles[coords] = prop;
            }
            else
            {
                prop.CurrentHealth = newHP;
                _obstacles[coords] = prop;
            }

            visualizer?.RefreshObstacles();

            string statusTransition = destroyed
                ? "➔ <b>TOTALEMENT DÉTRUIT</b> (Case dégagée et praticable)"
                : (transitioned ? $"➔ <b>ÉBRÉCHÉ / EFFONDRÉ</b> (Transition {prevCover} ➔ {newCover})" : $"({newHP}/{prop.MaxHealth} PV)");

            string multTag = mult != 1.0f ? $" x{mult:0.##} ({damageType})" : "";
            string soakTag = soaked > 0 ? $" − Seuil {soaked}" : "";

            result = new ObstacleDamageResult
            {
                Coordinates = coords,
                ObstacleName = prop.PropName,
                Material = prop.Material,
                RawDamage = rawDamage,
                HardnessSoaked = soaked,
                AppliedDamage = netDmg,
                PreviousHP = prevHP,
                CurrentHP = newHP,
                PreviousCover = prevCover,
                CurrentCover = newCover,
                TransitionedToHalf = transitioned,
                Destroyed = destroyed,
                CeilingCollapsed = false,
                Log = $"🧱 <b>IMPACT STRUCTUREL</b> : {prop.PropName} ({prop.Material}) subit [{rawDamage}{multTag}{soakTag} = <b>{netDmg} Dégâts</b>] {statusTransition}."
            };

            return true;
        }
    }
}

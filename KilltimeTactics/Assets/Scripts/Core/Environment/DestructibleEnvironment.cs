using System;
using UnityEngine;

namespace Killtime.Tactics.Grid
{
    public enum ObstacleMaterial
    {
        Bois,
        Pierre,
        Metal,
        Verre,
        Composite,
        Arcanotech,
        PlafondPlatre,
        Indestructible
    }

    public enum ObstacleDamageType
    {
        Generique,
        Contondant,
        Tranchant,
        Perforant,
        Explosif,
        Thermique,
        Plasma,
        Sonique
    }

    public struct MaterialProfile
    {
        public int BaseHP;
        public int Hardness;
        public float VulnerabilityMultiplier;
        public bool Flammable;

        public MaterialProfile(int baseHP, int hardness, float vuln = 1f, bool flammable = false)
        {
            BaseHP = baseHP;
            Hardness = hardness;
            VulnerabilityMultiplier = vuln;
            Flammable = flammable;
        }
    }

    public struct ObstacleDamageResult
    {
        public HexCoordinates Coordinates;
        public string ObstacleName;
        public ObstacleMaterial Material;
        public int RawDamage;
        public int HardnessSoaked;
        public int AppliedDamage;
        public int PreviousHP;
        public int CurrentHP;
        public CoverType PreviousCover;
        public CoverType CurrentCover;
        public bool TransitionedToHalf;
        public bool Destroyed;
        public bool CeilingCollapsed;
        public string Log;
    }

    public static class DestructibleEnvironmentRules
    {
        public static MaterialProfile GetProfile(ObstacleMaterial mat)
        {
            return mat switch
            {
                ObstacleMaterial.Bois => new MaterialProfile(16, 2, 1.0f, true),
                ObstacleMaterial.Pierre => new MaterialProfile(32, 6, 1.0f, false),
                ObstacleMaterial.Metal => new MaterialProfile(45, 10, 1.0f, false),
                ObstacleMaterial.Verre => new MaterialProfile(6, 0, 1.0f, false),
                ObstacleMaterial.Composite => new MaterialProfile(24, 4, 1.0f, false),
                ObstacleMaterial.Arcanotech => new MaterialProfile(60, 12, 1.0f, false),
                ObstacleMaterial.PlafondPlatre => new MaterialProfile(18, 3, 1.0f, false),
                ObstacleMaterial.Indestructible => new MaterialProfile(9999, 9999, 0f, false),
                _ => new MaterialProfile(20, 3, 1.0f, false)
            };
        }

        public static ObstacleMaterial DeduceMaterialFromName(string propName, CoverType cover)
        {
            if (string.IsNullOrEmpty(propName)) return ObstacleMaterial.Pierre;
            string lower = propName.ToLowerInvariant();

            if (lower.Contains("barricade_indestructible") || lower.Contains("bunker") || lower.Contains("bedrock"))
                return ObstacleMaterial.Indestructible;

            if (lower.Contains("caisse") || lower.Contains("crate") || lower.Contains("wood") || lower.Contains("bois") || lower.Contains("table") || lower.Contains("chaise"))
                return ObstacleMaterial.Bois;

            if (lower.Contains("metal") || lower.Contains("acier") || lower.Contains("steel") || lower.Contains("barrel") || lower.Contains("bidon") || lower.Contains("conteneur") || lower.Contains("door"))
                return ObstacleMaterial.Metal;

            if (lower.Contains("glass") || lower.Contains("verre") || lower.Contains("fenetre") || lower.Contains("vitre"))
                return ObstacleMaterial.Verre;

            if (lower.Contains("nytharite") || lower.Contains("arcano") || lower.Contains("shield") || lower.Contains("laser"))
                return ObstacleMaterial.Arcanotech;

            if (lower.Contains("pilier") || lower.Contains("pillar") || lower.Contains("muret") || lower.Contains("wall") || lower.Contains("mur") || lower.Contains("roche") || lower.Contains("stone") || lower.Contains("beton"))
                return ObstacleMaterial.Pierre;

            return cover == CoverType.Half ? ObstacleMaterial.Bois : ObstacleMaterial.Pierre;
        }

        public static float ComputeDamageMultiplier(ObstacleMaterial mat, ObstacleDamageType dmgType)
        {
            if (mat == ObstacleMaterial.Indestructible) return 0f;

            return (mat, dmgType) switch
            {
                (ObstacleMaterial.Bois, ObstacleDamageType.Tranchant) => 1.5f,
                (ObstacleMaterial.Bois, ObstacleDamageType.Thermique) => 2.0f,
                (ObstacleMaterial.Bois, ObstacleDamageType.Explosif) => 1.75f,
                (ObstacleMaterial.Bois, ObstacleDamageType.Perforant) => 0.75f,

                (ObstacleMaterial.Pierre, ObstacleDamageType.Contondant) => 1.5f,
                (ObstacleMaterial.Pierre, ObstacleDamageType.Explosif) => 1.5f,
                (ObstacleMaterial.Pierre, ObstacleDamageType.Tranchant) => 0.5f,
                (ObstacleMaterial.Pierre, ObstacleDamageType.Perforant) => 0.6f,

                (ObstacleMaterial.Metal, ObstacleDamageType.Plasma) => 2.0f,
                (ObstacleMaterial.Metal, ObstacleDamageType.Explosif) => 1.25f,
                (ObstacleMaterial.Metal, ObstacleDamageType.Perforant) => 0.5f,
                (ObstacleMaterial.Metal, ObstacleDamageType.Tranchant) => 0.25f,

                (ObstacleMaterial.Verre, _) => 2.0f,

                (ObstacleMaterial.PlafondPlatre, ObstacleDamageType.Explosif) => 2.0f,
                (ObstacleMaterial.PlafondPlatre, ObstacleDamageType.Contondant) => 1.5f,

                (_, ObstacleDamageType.Plasma) => 1.5f,
                (_, ObstacleDamageType.Explosif) => 1.25f,
                _ => 1.0f
            };
        }
    }
}
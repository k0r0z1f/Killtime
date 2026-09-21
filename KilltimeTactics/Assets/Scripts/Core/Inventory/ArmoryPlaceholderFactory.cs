using System;
using UnityEngine;

namespace Killtime.Core.Inventory
{
    /// <summary>
    /// Fabrique de modèles placeholder procéduraux pour l'armurerie complète.
    /// Aujourd'hui seuls les fusils existent en prefabs (Resources/Guns) :
    /// tout le reste (lames, laser, grenades, armures, pharma) est généré ici
    /// en primitives Unity, sans asset externe, pour le studio 3D et l'équipement visuel.
    /// </summary>
    public static class ArmoryPlaceholderFactory
    {
        public static bool HasRealPrefab(InventoryItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.PrefabPath)) return false;
            return LoadRealPrefab(item.PrefabPath) != null;
        }

        public static GameObject LoadRealPrefab(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return null;
            string clean = prefabPath;
            if (clean.StartsWith("Guns/", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(5);
            return Resources.Load<GameObject>($"Guns/{clean}")
                ?? Resources.Load<GameObject>(clean)
                ?? Resources.Load<GameObject>($"Objects/{clean}")
                ?? Resources.Load<GameObject>(prefabPath);
        }

        /// <summary>
        /// Charge le prefab réel si présent, sinon construit un placeholder procédural.
        /// Ne retourne jamais null (repli Generic).
        /// Grenades : variante visuelle par ère/effet (couleurs + manche).
        /// </summary>
        public static GameObject ResolveOrBuild(InventoryItem item, Transform parent)
        {
            string label = item != null ? item.Name : "Placeholder";
            string kind = item != null && !string.IsNullOrEmpty(item.PlaceholderKind)
                ? item.PlaceholderKind : "Generic";

            if (item != null && !string.IsNullOrEmpty(item.PrefabPath))
            {
                var prefab = LoadRealPrefab(item.PrefabPath);
                if (prefab != null)
                {
                    var go = UnityEngine.Object.Instantiate(prefab, parent);
                    go.name = "Real_" + label;
                    StripInteractiveComponents(go);
                    return go;
                }
            }
            if (kind == "Grenade" && item != null && item.IsGrenade)
                return BuildGrenadeVariant(parent, "Placeholder_" + label, item);
            return BuildByKind(kind, parent, "Placeholder_" + label);
        }

        public static GameObject BuildByKind(string kind, Transform parent, string objectName = "Placeholder")
        {
            if (string.IsNullOrEmpty(kind)) kind = "Generic";
            var root = new GameObject(string.IsNullOrEmpty(objectName) ? $"Placeholder_{kind}" : objectName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            switch (kind)
            {
                case "SwordMetal": BuildSwordMetal(root, 0.85f, false); break;
                case "SwordMetalLong": BuildSwordMetal(root, 1.25f, true); break;
                case "Club": BuildClub(root); break;
                case "Axe": BuildAxe(root); break;
                case "Hammer": BuildHammer(root); break;
                case "Spear": BuildSpear(root); break;
                case "Bow": BuildBow(root); break;
                case "LaserSword": BuildLaserSword(root, 0.85f, new Color(0.0f, 0.9f, 1.0f), false); break;
                case "LaserSwordLong": BuildLaserSword(root, 1.25f, new Color(0.6f, 0.2f, 1.0f), true); break;
                case "PistolLaser": BuildPistolLaser(root, new Color(0.15f, 0.5f, 0.9f)); break;
                case "RifleLaser": BuildRifleLaser(root, new Color(0.1f, 0.35f, 0.8f), 0.8f); break;
                case "SniperLaser": BuildRifleLaser(root, new Color(0.1f, 0.2f, 0.25f), 1.15f, true); break;
                case "Deglazer": BuildDeglazer(root); break;
                case "Grenade": BuildGrenade(root); break;
                case "GrenadeLauncher": BuildGrenadeLauncher(root); break;
                case "ArmorLight": BuildArmor(root, false); break;
                case "ArmorHeavy": BuildArmor(root, true); break;
                case "ShieldGen": BuildShieldGen(root); break;
                case "Syringe": BuildSyringe(root); break;
                case "Pills": BuildPills(root); break;
                case "Ration": BuildRation(root); break;
                case "Cell": BuildCell(root); break;
                default: BuildGenericCrate(root); break;
            }

            StripInteractiveComponents(root);
            return root;
        }

        // ================= BÂTIMENTS PAR FAMILLE =================

        private static void BuildSwordMetal(GameObject root, float bladeLen, bool twoHanded)
        {
            // Garde + poignée + lame acier
            // NOTE : les primitives Cylinder Unity font 2 m de haut pour scale.y=1 :
            // on divise donc tous les Y cylindres par 2 pour obtenir la hauteur voulue.
            Part(root, PrimitiveType.Cube, "Grip", new Vector3(0, -0.18f, 0), Quaternion.identity, new Vector3(0.05f, 0.22f, 0.05f), new Color(0.25f, 0.16f, 0.1f));
            Part(root, PrimitiveType.Cube, "Guard", new Vector3(0, -0.06f, 0), Quaternion.identity, new Vector3(0.22f, 0.03f, 0.06f), new Color(0.5f, 0.42f, 0.25f), metallic: 0.8f);
            Part(root, PrimitiveType.Cube, "Blade", new Vector3(0, bladeLen * 0.5f - 0.05f, 0), Quaternion.identity, new Vector3(0.07f, bladeLen, 0.02f), new Color(0.75f, 0.78f, 0.82f), metallic: 0.9f);
            Part(root, PrimitiveType.Cylinder, "Pommel", new Vector3(0, -0.31f, 0), Quaternion.identity, new Vector3(0.06f, 0.025f, 0.06f), new Color(0.5f, 0.42f, 0.25f), metallic: 0.8f);
            if (twoHanded)
                Part(root, PrimitiveType.Cube, "GripLong", new Vector3(0, -0.3f, 0), Quaternion.identity, new Vector3(0.045f, 0.18f, 0.045f), new Color(0.2f, 0.13f, 0.08f));
        }

        private static void BuildClub(GameObject root)
        {
            Part(root, PrimitiveType.Cylinder, "Stick", new Vector3(0, 0.15f, 0), Quaternion.Euler(0, 0, 8f), new Vector3(0.09f, 0.35f, 0.09f), new Color(0.4f, 0.27f, 0.15f));
            Part(root, PrimitiveType.Sphere, "Head", new Vector3(0.05f, 0.5f, 0), Quaternion.identity, new Vector3(0.16f, 0.2f, 0.16f), new Color(0.32f, 0.21f, 0.11f));
        }

        private static void BuildAxe(GameObject root)
        {
            Part(root, PrimitiveType.Cylinder, "Handle", new Vector3(0, 0.1f, 0), Quaternion.identity, new Vector3(0.06f, 0.45f, 0.06f), new Color(0.4f, 0.27f, 0.15f));
            Part(root, PrimitiveType.Cube, "Head", new Vector3(0.14f, 0.48f, 0), Quaternion.identity, new Vector3(0.3f, 0.18f, 0.04f), new Color(0.7f, 0.72f, 0.75f), metallic: 0.85f);
        }

        private static void BuildHammer(GameObject root)
        {
            Part(root, PrimitiveType.Cylinder, "Handle", new Vector3(0, 0.05f, 0), Quaternion.identity, new Vector3(0.07f, 0.5f, 0.07f), new Color(0.3f, 0.3f, 0.32f), metallic: 0.6f);
            Part(root, PrimitiveType.Cube, "Head", new Vector3(0, 0.55f, 0), Quaternion.identity, new Vector3(0.42f, 0.2f, 0.2f), new Color(0.45f, 0.47f, 0.5f), metallic: 0.85f);
        }

        private static void BuildSpear(GameObject root)
        {
            Part(root, PrimitiveType.Cylinder, "Shaft", new Vector3(0, 0.1f, 0), Quaternion.identity, new Vector3(0.05f, 0.8f, 0.05f), new Color(0.42f, 0.3f, 0.17f));
            Part(root, PrimitiveType.Cylinder, "Tip", new Vector3(0, 1.0f, 0), Quaternion.identity, new Vector3(0.09f, 0.15f, 0.09f), new Color(0.75f, 0.78f, 0.82f), metallic: 0.9f);
        }

        private static void BuildBow(GameObject root)
        {
            var limbMat = new Color(0.45f, 0.3f, 0.16f);
            Part(root, PrimitiveType.Cube, "LimbTop", new Vector3(0, 0.35f, 0), Quaternion.Euler(0, 0, 18f), new Vector3(0.05f, 0.7f, 0.05f), limbMat);
            Part(root, PrimitiveType.Cube, "LimbBot", new Vector3(0, -0.35f, 0), Quaternion.Euler(0, 0, -18f), new Vector3(0.05f, 0.7f, 0.05f), limbMat);
            Part(root, PrimitiveType.Cube, "Grip", new Vector3(0, 0, 0), Quaternion.identity, new Vector3(0.07f, 0.18f, 0.07f), new Color(0.2f, 0.13f, 0.08f));
            Part(root, PrimitiveType.Cube, "String", new Vector3(0.12f, 0, 0), Quaternion.identity, new Vector3(0.008f, 1.25f, 0.008f), new Color(0.9f, 0.9f, 0.9f));
            Part(root, PrimitiveType.Cube, "Arrow", new Vector3(0.05f, 0, 0), Quaternion.Euler(0, 0, 90f), new Vector3(0.02f, 0.7f, 0.02f), new Color(0.8f, 0.7f, 0.5f));
        }

        private static void BuildLaserSword(GameObject root, float bladeLen, Color bladeColor, bool twoHanded)
        {
            Part(root, PrimitiveType.Cylinder, "Hilt", new Vector3(0, -0.2f, 0), Quaternion.identity, new Vector3(0.09f, 0.15f, 0.09f), new Color(0.15f, 0.16f, 0.18f), metallic: 0.7f);
            Part(root, PrimitiveType.Cylinder, "Emitter", new Vector3(0, -0.03f, 0), Quaternion.identity, new Vector3(0.12f, 0.03f, 0.12f), new Color(0.0f, 0.85f, 1.0f), emissive: new Color(0.0f, 0.7f, 0.9f));
            Part(root, PrimitiveType.Cube, "Blade", new Vector3(0, bladeLen * 0.5f, 0), Quaternion.identity, new Vector3(0.055f, bladeLen, 0.055f), bladeColor, emissive: bladeColor * 1.6f);
            Part(root, PrimitiveType.Sphere, "TipGlow", new Vector3(0, bladeLen, 0), Quaternion.identity, new Vector3(0.07f, 0.07f, 0.07f), Color.white, emissive: bladeColor * 2f);
            if (twoHanded)
                Part(root, PrimitiveType.Cylinder, "HiltExt", new Vector3(0, -0.42f, 0), Quaternion.identity, new Vector3(0.08f, 0.11f, 0.08f), new Color(0.12f, 0.12f, 0.14f), metallic: 0.7f);
        }

        private static void BuildPistolLaser(GameObject root, Color body)
        {
            Part(root, PrimitiveType.Cube, "Body", new Vector3(0, 0.05f, 0.1f), Quaternion.identity, new Vector3(0.09f, 0.12f, 0.4f), body, metallic: 0.5f);
            Part(root, PrimitiveType.Cube, "Grip", new Vector3(0, -0.12f, -0.02f), Quaternion.Euler(-18f, 0, 0), new Vector3(0.07f, 0.24f, 0.09f), new Color(0.12f, 0.12f, 0.14f));
            Part(root, PrimitiveType.Cylinder, "Cell", new Vector3(0, 0.1f, 0.02f), Quaternion.Euler(90f, 0, 0), new Vector3(0.06f, 0.05f, 0.06f), new Color(0.0f, 0.9f, 1.0f), emissive: new Color(0.0f, 0.8f, 1.0f));
            Part(root, PrimitiveType.Cube, "Sight", new Vector3(0, 0.13f, 0.05f), Quaternion.identity, new Vector3(0.03f, 0.05f, 0.1f), new Color(0.08f, 0.08f, 0.1f));
        }

        private static void BuildRifleLaser(GameObject root, Color body, float length, bool scoped = false)
        {
            Part(root, PrimitiveType.Cube, "Body", new Vector3(0, 0.05f, 0.15f), Quaternion.identity, new Vector3(0.1f, 0.14f, 0.7f * length), body, metallic: 0.55f);
            Part(root, PrimitiveType.Cube, "Barrel", new Vector3(0, 0.06f, 0.55f * length + 0.15f), Quaternion.identity, new Vector3(0.05f, 0.05f, 0.35f), new Color(0.1f, 0.1f, 0.12f), metallic: 0.7f);
            Part(root, PrimitiveType.Cube, "Stock", new Vector3(0, 0.0f, -0.28f), Quaternion.identity, new Vector3(0.09f, 0.18f, 0.25f), new Color(0.12f, 0.12f, 0.14f));
            Part(root, PrimitiveType.Cube, "Grip", new Vector3(0, -0.13f, 0.05f), Quaternion.Euler(-18f, 0, 0), new Vector3(0.07f, 0.22f, 0.09f), new Color(0.12f, 0.12f, 0.14f));
            Part(root, PrimitiveType.Cylinder, "Cell", new Vector3(0, 0.12f, 0.1f), Quaternion.Euler(90f, 0, 0), new Vector3(0.07f, 0.06f, 0.07f), new Color(0.0f, 0.9f, 1.0f), emissive: new Color(0.0f, 0.8f, 1.0f));
            if (scoped)
            {
                Part(root, PrimitiveType.Cylinder, "Scope", new Vector3(0, 0.16f, 0.12f), Quaternion.Euler(90f, 0, 0), new Vector3(0.06f, 0.125f, 0.06f), new Color(0.08f, 0.08f, 0.1f), metallic: 0.6f);
                Part(root, PrimitiveType.Cylinder, "Lens", new Vector3(0, 0.16f, 0.25f), Quaternion.Euler(90f, 0, 0), new Vector3(0.05f, 0.01f, 0.05f), new Color(0.2f, 0.8f, 1.0f), emissive: new Color(0.2f, 0.7f, 1.0f));
            }
        }

        private static void BuildDeglazer(GameObject root)
        {
            BuildRifleLaser(root, new Color(0.85f, 0.85f, 0.88f), 1.1f, true);
            // Prisme nytharite distinctif
            Part(root, PrimitiveType.Cube, "Prism", new Vector3(0, 0.12f, 0.3f), Quaternion.Euler(0, 45f, 0), new Vector3(0.12f, 0.16f, 0.12f), new Color(0.6f, 0.2f, 1.0f), emissive: new Color(0.55f, 0.15f, 1.0f) * 1.8f);
            Part(root, PrimitiveType.Cube, "GoldTrim", new Vector3(0, -0.03f, 0.15f), Quaternion.identity, new Vector3(0.11f, 0.02f, 0.5f), new Color(0.85f, 0.65f, 0.2f), metallic: 0.9f);
        }

        private static void BuildGrenade(GameObject root)
        {
            Part(root, PrimitiveType.Sphere, "Body", new Vector3(0, 0, 0), Quaternion.identity, new Vector3(0.22f, 0.26f, 0.22f), new Color(0.25f, 0.35f, 0.25f), metallic: 0.6f);
            Part(root, PrimitiveType.Cylinder, "Fuse", new Vector3(0, 0.17f, 0), Quaternion.identity, new Vector3(0.08f, 0.04f, 0.08f), new Color(0.6f, 0.6f, 0.62f), metallic: 0.8f);
            Part(root, PrimitiveType.Cylinder, "Ring", new Vector3(0.08f, 0.22f, 0), Quaternion.Euler(0, 0, 90f), new Vector3(0.09f, 0.01f, 0.09f), new Color(0.75f, 0.75f, 0.75f), metallic: 0.9f);
            Part(root, PrimitiveType.Cube, "Stripe", new Vector3(0, 0.02f, 0), Quaternion.identity, new Vector3(0.23f, 0.05f, 0.23f), new Color(0.9f, 0.2f, 0.15f), emissive: new Color(0.7f, 0.1f, 0.1f));
        }

        /// <summary>
        /// Variante visuelle par ère / effet : coque teintée + bandeau d'effet +
        /// manche bois pour les Stielhandgranate, prisme nytharite pour l'Arcanotech.
        /// </summary>
        private static GameObject BuildGrenadeVariant(Transform parent, string objectName, InventoryItem item)
        {
            var root = new GameObject(string.IsNullOrEmpty(objectName) ? "Placeholder_Grenade" : objectName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            string era = (item.Era ?? "").ToLowerInvariant();
            string kind = (item.GrenadeKind ?? "").ToLowerInvariant();
            string nm = (item.Name ?? "").ToLowerInvariant();

            // Coque par ère.
            Color body = new Color(0.25f, 0.35f, 0.25f);
            if (era.Contains("poudre")) body = new Color(0.35f, 0.22f, 0.12f);
            else if (era.Contains("grande")) body = new Color(0.32f, 0.32f, 0.28f);
            else if (era.Contains("seconde")) body = new Color(0.30f, 0.34f, 0.22f);
            else if (era.Contains("froide")) body = new Color(0.22f, 0.30f, 0.22f);
            else if (era.Contains("modern")) body = new Color(0.20f, 0.28f, 0.20f);
            else if (era.Contains("futur")) body = new Color(0.16f, 0.22f, 0.34f);
            else if (era.Contains("arcano")) body = new Color(0.24f, 0.16f, 0.34f);

            // Bandeau par effet.
            Color stripe = new Color(0.9f, 0.2f, 0.15f);
            Color glow = new Color(0.7f, 0.1f, 0.1f);
            if (kind.Contains("fumi") || kind.Contains("smoke")) { stripe = new Color(0.85f, 0.85f, 0.88f); glow = new Color(0.5f, 0.5f, 0.55f); }
            else if (kind.Contains("flash") || kind.Contains("stun") || kind.Contains("sonique")) { stripe = Color.white; glow = new Color(0.9f, 0.9f, 0.9f); }
            else if (kind.Contains("incend") || kind.Contains("thermobar") || kind.Contains("thermite")) { stripe = new Color(1f, 0.45f, 0.05f); glow = new Color(0.9f, 0.3f, 0.05f); }
            else if (kind.Contains("gaz") || kind.Contains("gas")) { stripe = new Color(0.55f, 0.9f, 0.25f); glow = new Color(0.35f, 0.7f, 0.15f); }
            else if (kind.Contains("emp") || kind.Contains("iem") || kind.Contains("ion")) { stripe = new Color(0.3f, 0.7f, 1f); glow = new Color(0.2f, 0.6f, 1f); }
            else if (kind.Contains("plasma")) { stripe = new Color(0.6f, 0.3f, 1f); glow = new Color(0.5f, 0.2f, 1f); }
            else if (kind.Contains("cryo") || kind.Contains("gel")) { stripe = new Color(0.55f, 0.85f, 1f); glow = new Color(0.4f, 0.75f, 1f); }
            else if (kind.Contains("grav")) { stripe = new Color(0.75f, 0.35f, 1f); glow = new Color(0.6f, 0.25f, 1f); }
            else if (kind.Contains("exercice") || kind.Contains("inerte")) { stripe = new Color(0.3f, 0.5f, 1f); glow = new Color(0.2f, 0.4f, 0.9f); }

            bool stick = nm.Contains("stiel") || nm.Contains("manche") || nm.Contains("m24") || nm.Contains("m1915");
            if (stick)
            {
                // Grenade à manche : tête cylindrique + long manche bois.
                Part(root, PrimitiveType.Cylinder, "Head", new Vector3(0, 0.32f, 0), Quaternion.identity, new Vector3(0.20f, 0.15f, 0.20f), body, metallic: 0.55f);
                Part(root, PrimitiveType.Cube, "Stripe", new Vector3(0, 0.32f, 0), Quaternion.identity, new Vector3(0.21f, 0.06f, 0.21f), stripe, emissive: glow);
                Part(root, PrimitiveType.Cylinder, "Stick", new Vector3(0, -0.05f, 0), Quaternion.identity, new Vector3(0.09f, 0.275f, 0.09f), new Color(0.45f, 0.30f, 0.15f));
                Part(root, PrimitiveType.Cylinder, "Cap", new Vector3(0, -0.34f, 0), Quaternion.identity, new Vector3(0.10f, 0.03f, 0.10f), new Color(0.6f, 0.6f, 0.62f), metallic: 0.8f);
            }
            else
            {
                Part(root, PrimitiveType.Sphere, "Body", new Vector3(0, 0, 0), Quaternion.identity, new Vector3(0.22f, 0.26f, 0.22f), body, metallic: 0.6f);
                Part(root, PrimitiveType.Cylinder, "Fuse", new Vector3(0, 0.17f, 0), Quaternion.identity, new Vector3(0.08f, 0.04f, 0.08f), new Color(0.6f, 0.6f, 0.62f), metallic: 0.8f);
                Part(root, PrimitiveType.Cylinder, "Ring", new Vector3(0.08f, 0.22f, 0), Quaternion.Euler(0, 0, 90f), new Vector3(0.09f, 0.01f, 0.09f), new Color(0.75f, 0.75f, 0.75f), metallic: 0.9f);
                Part(root, PrimitiveType.Cube, "Stripe", new Vector3(0, 0.02f, 0), Quaternion.identity, new Vector3(0.23f, 0.05f, 0.23f), stripe, emissive: glow);
            }

            // Prisme nytharite distinctif pour l'Arcanotech.
            if (era.Contains("arcano") || nm.Contains("nytharite") || nm.Contains("graviton") || nm.Contains("stase"))
            {
                Part(root, PrimitiveType.Cube, "Prism", new Vector3(0, 0.02f, 0.13f), Quaternion.Euler(0, 30f, 0), new Vector3(0.08f, 0.10f, 0.05f), new Color(0.6f, 0.25f, 1f), emissive: new Color(0.55f, 0.2f, 1f) * 1.8f);
            }
            // Ananas pour la Mk2.
            if (nm.Contains("pineapple") || nm.Contains("mk2"))
            {
                for (int i = 0; i < 4; i++)
                    Part(root, PrimitiveType.Cube, $"Rib{i}", new Vector3(0, -0.06f + i * 0.07f, 0), Quaternion.identity, new Vector3(0.24f, 0.02f, 0.24f), body * 0.85f, metallic: 0.6f);
            }

            StripInteractiveComponents(root);
            return root;
        }

        private static void BuildGrenadeLauncher(GameObject root)
        {
            // Lance-grenades 40mm : gros tube + crosse + poignée + anneau ambre.
            Part(root, PrimitiveType.Cylinder, "Tube", new Vector3(0, 0.05f, 0.15f), Quaternion.Euler(90f, 0, 0), new Vector3(0.16f, 0.325f, 0.16f), new Color(0.16f, 0.17f, 0.19f), metallic: 0.7f);
            Part(root, PrimitiveType.Cylinder, "Muzzle", new Vector3(0, 0.05f, 0.5f), Quaternion.Euler(90f, 0, 0), new Vector3(0.19f, 0.04f, 0.19f), new Color(0.10f, 0.10f, 0.12f), metallic: 0.8f);
            Part(root, PrimitiveType.Cube, "Stock", new Vector3(0, 0.0f, -0.28f), Quaternion.identity, new Vector3(0.10f, 0.20f, 0.28f), new Color(0.30f, 0.20f, 0.12f));
            Part(root, PrimitiveType.Cube, "Grip", new Vector3(0, -0.14f, 0.02f), Quaternion.Euler(-18f, 0, 0), new Vector3(0.08f, 0.24f, 0.10f), new Color(0.12f, 0.12f, 0.14f));
            Part(root, PrimitiveType.Cylinder, "Ring", new Vector3(0, 0.05f, 0.32f), Quaternion.Euler(90f, 0, 0), new Vector3(0.18f, 0.025f, 0.18f), new Color(1.0f, 0.65f, 0.15f), emissive: new Color(0.9f, 0.5f, 0.1f));
            Part(root, PrimitiveType.Cube, "Sight", new Vector3(0, 0.18f, 0.05f), Quaternion.identity, new Vector3(0.04f, 0.12f, 0.06f), new Color(0.08f, 0.08f, 0.10f));
        }

        private static void BuildArmor(GameObject root, bool heavy)
        {
            Color plate = heavy ? new Color(0.35f, 0.37f, 0.42f) : new Color(0.2f, 0.35f, 0.45f);
            Part(root, PrimitiveType.Cube, "Chest", new Vector3(0, 0.1f, 0), Quaternion.identity, new Vector3(0.5f, 0.6f, 0.28f), plate, metallic: heavy ? 0.8f : 0.3f);
            Part(root, PrimitiveType.Cube, "Plate", new Vector3(0, 0.15f, 0.16f), Quaternion.identity, new Vector3(0.4f, 0.45f, 0.04f), new Color(0.15f, 0.5f, 0.65f), metallic: 0.4f);
            Part(root, PrimitiveType.Cube, "ShoulderL", new Vector3(-0.32f, 0.35f, 0), Quaternion.identity, new Vector3(0.16f, 0.12f, 0.24f), plate, metallic: 0.6f);
            Part(root, PrimitiveType.Cube, "ShoulderR", new Vector3(0.32f, 0.35f, 0), Quaternion.identity, new Vector3(0.16f, 0.12f, 0.24f), plate, metallic: 0.6f);
            if (heavy)
            {
                Part(root, PrimitiveType.Cube, "Plackart", new Vector3(0, -0.25f, 0.05f), Quaternion.identity, new Vector3(0.44f, 0.2f, 0.26f), new Color(0.25f, 0.27f, 0.32f), metallic: 0.8f);
                Part(root, PrimitiveType.Cylinder, "Light", new Vector3(0.15f, 0.3f, 0.16f), Quaternion.Euler(90f, 0, 0), new Vector3(0.05f, 0.015f, 0.05f), new Color(1.0f, 0.6f, 0.1f), emissive: new Color(1.0f, 0.5f, 0.1f));
            }
        }

        private static void BuildShieldGen(GameObject root)
        {
            Part(root, PrimitiveType.Cylinder, "Base", new Vector3(0, -0.15f, 0), Quaternion.identity, new Vector3(0.25f, 0.05f, 0.25f), new Color(0.12f, 0.13f, 0.16f), metallic: 0.7f);
            Part(root, PrimitiveType.Sphere, "Core", new Vector3(0, 0.1f, 0), Quaternion.identity, new Vector3(0.2f, 0.28f, 0.2f), new Color(0.0f, 0.85f, 1.0f), emissive: new Color(0.0f, 0.75f, 1.0f) * 1.7f);
            Part(root, PrimitiveType.Sphere, "Bubble", new Vector3(0, 0.1f, 0), Quaternion.identity, new Vector3(0.55f, 0.65f, 0.55f), new Color(0.0f, 0.7f, 1.0f, 0.18f), transparent: true);
        }

        private static void BuildSyringe(GameObject root)
        {
            Part(root, PrimitiveType.Cylinder, "Tube", new Vector3(0, 0.05f, 0), Quaternion.identity, new Vector3(0.12f, 0.2f, 0.12f), new Color(0.9f, 0.92f, 0.95f, 0.7f), transparent: true);
            Part(root, PrimitiveType.Cylinder, "Liquid", new Vector3(0, -0.02f, 0), Quaternion.identity, new Vector3(0.09f, 0.11f, 0.09f), new Color(0.1f, 0.9f, 0.5f), emissive: new Color(0.1f, 0.7f, 0.4f));
            Part(root, PrimitiveType.Cylinder, "Needle", new Vector3(0, -0.28f, 0), Quaternion.identity, new Vector3(0.02f, 0.1f, 0.02f), new Color(0.75f, 0.75f, 0.78f), metallic: 0.9f);
            Part(root, PrimitiveType.Cube, "Plunger", new Vector3(0, 0.28f, 0), Quaternion.identity, new Vector3(0.14f, 0.04f, 0.14f), new Color(0.15f, 0.15f, 0.18f));
        }

        private static void BuildPills(GameObject root)
        {
            Part(root, PrimitiveType.Cube, "Box", new Vector3(0, -0.05f, 0), Quaternion.identity, new Vector3(0.35f, 0.12f, 0.25f), new Color(0.92f, 0.92f, 0.94f));
            Part(root, PrimitiveType.Cube, "Label", new Vector3(0, 0.02f, 0), Quaternion.identity, new Vector3(0.3f, 0.01f, 0.2f), new Color(0.9f, 0.25f, 0.3f));
            for (int i = 0; i < 4; i++)
                Part(root, PrimitiveType.Capsule, $"Tab{i}", new Vector3(-0.12f + i * 0.08f, 0.08f, 0), Quaternion.Euler(0, 0, 90f), new Vector3(0.05f, 0.08f, 0.05f), i % 2 == 0 ? Color.white : new Color(0.2f, 0.6f, 1.0f));
        }

        private static void BuildRation(GameObject root)
        {
            Part(root, PrimitiveType.Cube, "Pack", new Vector3(0, 0, 0), Quaternion.identity, new Vector3(0.35f, 0.25f, 0.2f), new Color(0.55f, 0.45f, 0.25f));
            Part(root, PrimitiveType.Cube, "Seal", new Vector3(0, 0.08f, 0), Quaternion.identity, new Vector3(0.36f, 0.05f, 0.21f), new Color(0.3f, 0.25f, 0.15f));
            Part(root, PrimitiveType.Cube, "Label", new Vector3(0, 0, 0.11f), Quaternion.identity, new Vector3(0.25f, 0.12f, 0.01f), new Color(0.15f, 0.6f, 0.4f));
        }

        private static void BuildCell(GameObject root)
        {
            Part(root, PrimitiveType.Cylinder, "Canister", new Vector3(0, 0, 0), Quaternion.identity, new Vector3(0.2f, 0.2f, 0.2f), new Color(0.18f, 0.19f, 0.22f), metallic: 0.7f);
            Part(root, PrimitiveType.Cube, "Crystal", new Vector3(0, 0.05f, 0), Quaternion.Euler(0, 30f, 0), new Vector3(0.12f, 0.25f, 0.12f), new Color(0.6f, 0.25f, 1.0f), emissive: new Color(0.55f, 0.2f, 1.0f) * 1.8f);
            Part(root, PrimitiveType.Cylinder, "Cap", new Vector3(0, 0.24f, 0), Quaternion.identity, new Vector3(0.22f, 0.025f, 0.22f), new Color(0.85f, 0.65f, 0.2f), metallic: 0.9f);
        }

        private static void BuildGenericCrate(GameObject root)
        {
            Part(root, PrimitiveType.Cube, "Crate", new Vector3(0, 0, 0), Quaternion.identity, new Vector3(0.4f, 0.4f, 0.4f), new Color(0.4f, 0.35f, 0.25f));
            Part(root, PrimitiveType.Cube, "Tape", new Vector3(0, 0, 0), Quaternion.identity, new Vector3(0.42f, 0.1f, 0.42f), new Color(0.0f, 0.85f, 1.0f), emissive: new Color(0.0f, 0.5f, 0.7f));
        }

        // ================= OUTILS =================

        private static void StripInteractiveComponents(GameObject root)
        {
            if (root == null) return;
            var cols = root.GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++) UnityEngine.Object.DestroyImmediate(cols[i]);
            var anims = root.GetComponentsInChildren<Animator>();
            for (int i = 0; i < anims.Length; i++) anims[i].enabled = false;
            var rbs = root.GetComponentsInChildren<Rigidbody>();
            for (int i = 0; i < rbs.Length; i++) UnityEngine.Object.DestroyImmediate(rbs[i]);
        }

        private static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Quaternion rot, Vector3 scale, Color color, float metallic = 0.1f, Color? emissive = null, bool transparent = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);

            var mat = CreateMaterial($"{parent.name}_{name}", color, metallic, emissive, transparent);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = mat;
            return go;
        }

        private static Material CreateMaterial(string name, Color color, float metallic, Color? emissive, bool transparent)
        {
            Shader shader = null;
            try
            {
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (rp != null)
                {
                    string rpName = rp.GetType().Name;
                    if (rpName.Contains("Universal") || rpName.Contains("URP"))
                        shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                    else if (rpName.Contains("HighDefinition") || rpName.Contains("HDRP"))
                        shader = Shader.Find("HDRP/Lit");
                }
            }
            catch { shader = null; }

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "Placeholder_" + name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.5f);
            if (emissive.HasValue)
            {
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.SetColor("_EmissionColor", emissive.Value);
                    mat.EnableKeyword("_EMISSION");
                }
            }
            if (transparent)
            {
                Color c = color; c.a = Mathf.Clamp01(color.a <= 0.01f ? 0.18f : color.a);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                if (mat.HasProperty("_Surface"))
                {
                    mat.SetFloat("_Surface", 1f);
                    mat.SetFloat("_Blend", 0f);
                }
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }
            return mat;
        }
    }
}

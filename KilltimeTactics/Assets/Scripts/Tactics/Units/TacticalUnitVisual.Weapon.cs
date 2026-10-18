using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Killtime.Tactics.Units;
using Killtime.Tactics.CombatUI;
using Killtime.Tactics.Grid;
using Killtime.Core.Character;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Armes : sockets, grips, normalisation taille, drop physique.
    /// Façade : TacticalUnitVisual.cs (champs sérialisés, lifecycle). Aucune API changée.
    /// </summary>
    public partial class TacticalUnitVisual : MonoBehaviour
    {
        public bool HasRifleEquipped()
        {
            var weapon = _unit != null && _unit.Sheet != null ? _unit.Sheet.GetEquippedWeapon() : null;
            return IsRifleWeapon(weapon);
        }

        public static bool IsRifleWeapon(Core.Inventory.InventoryItem item)
        {
            if (item == null || item.Type != Core.Inventory.ItemType.Weapon) return false;
            if (item.IsGrenade || item.IsThrowableGrenade()) return false;
            if (SkillDefinitions.IsMeleeWeaponSkill(item.AssociatedSkill)) return false;

            var profile = Killtime.Core.Inventory.WeaponGripService.ResolveProfile(item);
            if (profile != null && profile.Socket == Killtime.Core.Inventory.WeaponGripSocket.ChestTwoHands)
                return true;

            if (item.AssociatedSkill == SkillType.Ballistique) return true;
            if (item.EquipSlot == Core.Inventory.ItemEquipSlot.TwoHands) return true;

            string n = ((item.Name ?? "") + " " + (item.PrefabPath ?? "")).ToLowerInvariant();
            return n.Contains("rifle") || n.Contains("gun") || n.Contains("fusil") || n.Contains("shotgun") || n.Contains("sniper") || n.Contains("pistolet") || n.Contains("pistol");
        }

        /// <summary>
        /// Arme de mêlée équipée (épée métal/laser, hache, marteau, pique...) : skills
        /// ManiementArmes / ArmesPercantes (Armes Contondantes = spé de Maniement),
        /// hors fusils, grenades et lanceurs.
        /// Mains nues (aucune arme) => false : le coup de pied "Roundkick" reste l'anim à mains nues.
        /// </summary>
        public static bool IsMeleeWeapon(Core.Inventory.InventoryItem item)
        {
            if (item == null || item.Type != Core.Inventory.ItemType.Weapon) return false;
            if (item.IsGrenade || item.IsThrowableGrenade() || item.IsLauncher) return false;
            if (IsRifleWeapon(item)) return false;
            return SkillDefinitions.IsMeleeWeaponSkill(item.AssociatedSkill);
        }

        public bool HasMeleeWeaponEquipped()
        {
            var weapon = _unit != null && _unit.Sheet != null ? _unit.Sheet.GetEquippedWeapon() : null;
            return IsMeleeWeapon(weapon);
        }

        /// <summary>
        /// Valeurs du paramètre Animator "WeaponType" (Int).
        /// 0 = poings / lame légère (Fight Idle), 1 = fusil (Rifle Idle),
        /// 2 = arme contondante lourde matraque / hache / marteau (Weapon Fight Idle).
        /// </summary>
        public const int WeaponTypeUnarmedLight = 0;
        public const int WeaponTypeRifle = 1;
        public const int WeaponTypeHeavyMelee = 2;
        public const string WeaponFightIdleStateName = "Weapon Fight Idle";
        public const string FightIdleStateName = "Fight Idle";
        public const string RifleIdleStateName = "Rifle Idle";

        /// <summary>
        /// Arme lourde à garde dédiée (matraque / gourdin, hache, marteau / masse) :
        /// sous-ensemble de <see cref="IsMeleeWeapon"/> avec PlaceholderKind Club / Axe / Hammer
        /// (fallback nom : gourdin, matraque, hache, marteau, masse...).
        /// Les épées / piques / lames restent sur WeaponType 0 (Fight Idle).
        /// </summary>
        public static bool IsHeavyMeleeWeapon(Core.Inventory.InventoryItem item)
        {
            if (item == null || item.Type != Core.Inventory.ItemType.Weapon) return false;
            if (item.IsGrenade || item.IsThrowableGrenade() || item.IsLauncher) return false;
            if (IsRifleWeapon(item)) return false;
            if (!IsMeleeWeapon(item)) return false;

            string kind = (item.PlaceholderKind ?? "").Trim();
            if (string.Equals(kind, "Club", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "Axe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "Hammer", StringComparison.OrdinalIgnoreCase))
                return true;

            string n = ((item.Name ?? "") + " " + (item.PrefabPath ?? "") + " " + kind).ToLowerInvariant();
            if (n.Contains("gourdin") || n.Contains("matraque") || n.Contains("club")) return true;
            if (n.Contains("hache") || n.Contains("bardiche") || n.Contains("cogn")) return true;
            // "axe" seul est trop large en anglais (pickaxe...) : on exige un contexte d'arme.
            if (n.Contains("battle axe") || n.Contains("war axe") || n.Contains("war-axe") || n.Contains(" hache")) return true;
            if (n.Contains("marteau") || n.Contains("hammer") || n.Contains("masse") || n.Contains("massue") || n.Contains("mace") || n.Contains("fléau")) return true;
            return false;
        }

        public bool HasHeavyMeleeEquipped()
        {
            var weapon = _unit != null && _unit.Sheet != null ? _unit.Sheet.GetEquippedWeapon() : null;
            return IsHeavyMeleeWeapon(weapon);
        }

        /// <summary>
        /// Type d'arme pour l'Animator : 1 = fusil (prioritaire), 2 = matraque/hache/marteau, 0 = reste.
        /// </summary>
        public static int GetWeaponType(Core.Inventory.InventoryItem item)
        {
            if (IsRifleWeapon(item)) return WeaponTypeRifle;
            if (IsHeavyMeleeWeapon(item)) return WeaponTypeHeavyMelee;
            return WeaponTypeUnarmedLight;
        }

        public int GetCurrentWeaponType()
        {
            var weapon = _unit != null && _unit.Sheet != null ? _unit.Sheet.GetEquippedWeapon() : null;
            return GetWeaponType(weapon);
        }

        /// <summary>
        /// État idle de combat correspondant à l'arme : Rifle Idle / Weapon Fight Idle / Fight Idle.
        /// </summary>
        public static string GetCombatIdleStateName(Core.Inventory.InventoryItem item)
        {
            int t = GetWeaponType(item);
            if (t == WeaponTypeRifle) return RifleIdleStateName;
            if (t == WeaponTypeHeavyMelee) return WeaponFightIdleStateName;
            return FightIdleStateName;
        }

        public string GetCurrentCombatIdleStateName()
        {
            var weapon = _unit != null && _unit.Sheet != null ? _unit.Sheet.GetEquippedWeapon() : null;
            return GetCombatIdleStateName(weapon);
        }

        private const string ForcedRefreshSentinel = "__FORCE_REFRESH__";

        public void RefreshEquippedWeaponVisual()
        {
            _lastEquippedItemId = ForcedRefreshSentinel;
            UpdateEquippedWeaponVisual();
        }

        private void UpdateEquippedWeaponVisual()
        {
            if (_unit == null || _unit.Sheet == null) return;

            var equipped = _unit.Sheet.GetEquippedWeapon();
            string currentId = equipped != null ? equipped.ItemId : null;

            if (currentId == _lastEquippedItemId) return;

            _lastEquippedItemId = currentId;

            if (equipped == null)
            {
                if (_putAwayRoutine != null)
                {
                    StopCoroutine(_putAwayRoutine);
                    _putAwayRoutine = null;
                }
                ClearEquippedWeaponInstance();
                _currentWeaponIsRifle = false;
                _currentWeaponIsHeavyMelee = false;
                if (_animator != null)
                {
                    _animator.SetInteger(AnimWeaponType, WeaponTypeUnarmedLight);
                }
            }
            else
            {
                AttachEquippedWeapon(equipped);
            }
        }

        private void AttachEquippedWeapon(Core.Inventory.InventoryItem item)
        {
            if (_putAwayRoutine != null)
            {
                StopCoroutine(_putAwayRoutine);
                _putAwayRoutine = null;
            }
            ClearEquippedWeaponInstance();
#if UNITY_EDITOR
            _hasAppliedWeaponPose = false;
#endif

            if (item == null) return;

            // Les consommables / munitions / misc ne s'attachent pas en main.
            if (item.Type != Core.Inventory.ItemType.Weapon) return;

            bool isRifle = IsRifleWeapon(item);
            bool isPistol = !isRifle && !IsMeleeWeapon(item)
                && item.AssociatedSkill == SkillType.Ballistique
                && item.EquipSlot != Core.Inventory.ItemEquipSlot.TwoHands;

            // Main droite = main de détente pour mêlée + pistolet.
            // Fusil 2M : ancrage poitrine (poigne à deux mains centrée, stable entre
            // les rigs Mixamo/Blender). La pose "Rifle Idle" amène les deux mains
            // autour de l'arme au lieu d'un seul poing en diagonale.
            Transform socket = isRifle ? GetRifleSocket() : GetRightHandSocket();

            GameObject instance = null;
            Vector3 prefabAuthoredScale = Vector3.one;
            if (!string.IsNullOrEmpty(item.PrefabPath))
            {
                var prefab = LoadWeaponPrefab(item.PrefabPath);
                if (prefab != null)
                {
                    prefabAuthoredScale = prefab.transform.localScale;
                    // worldPositionStays=false : le localScale capturé reste celui de l'auteur.
                    // L'ancien Instantiate(prefab, socket) gardait l'échelle monde (true par défaut)
                    // puis la compensation la re-divisait -> arme x5 sur Soldier (socket x0.18).
                    instance = Instantiate(prefab, socket, false);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    instance.transform.localScale = prefabAuthoredScale;
                }
            }
            // Repli : placeholder procédural (armurerie complète sans prefabs).
            if (instance == null)
            {
                instance = Core.Inventory.ArmoryPlaceholderFactory.ResolveOrBuild(item, socket);
                if (instance.transform.parent != socket) instance.transform.SetParent(socket, false);
                prefabAuthoredScale = Vector3.one;
            }
            _equippedWeaponInstance = instance;
            _equippedWeaponInstance.name = "Equipped_" + item.Name;

            // ---- Poigne : orientation ----
            // Mêlée : garde existante (lame vers le haut/avant dans le poing droit).
            // Pistolet : canon à l'horizontale vers l'avant, poignée dans la paume droite.
            // Fusil : canon à l'horizontale vers l'avant depuis la poitrine, crosse vers
            // l'épaule droite, poignée sous la main droite — poigne à deux mains.
            // On aligne le monde (avant personnage) plutôt qu'un Euler fixe, car l'orientation
            // de l'os main varie selon le modèle (Soldier Mixamo vs Mina Blender) : un Euler
            // unique donnait la diagonale "flottante" du screenshot sur Soldier.
            var gripProfile = Killtime.Core.Inventory.WeaponGripService.ResolveProfile(item);
            socket = GetSocketTransform(gripProfile.Socket);
            if (_equippedWeaponInstance.transform.parent != socket)
            {
                _equippedWeaponInstance.transform.SetParent(socket, false);
            }

            _equippedWeaponInstance.transform.localPosition = Killtime.Core.Inventory.WeaponGripService.ComputeWeaponLocalPosition(socket, transform, gripProfile, _unitScale);
            _equippedWeaponInstance.transform.localRotation = Killtime.Core.Inventory.WeaponGripService.ComputeWeaponLocalRotation(instance, socket, transform, gripProfile, item);
            _equippedWeaponInstance.transform.localScale = Killtime.Core.Inventory.WeaponGripService.ComputeWeaponLocalScale(instance, socket, gripProfile, item, _unitScale, transform);

#if UNITY_EDITOR
            _appliedWeaponLocalPos = _equippedWeaponInstance.transform.localPosition;
            _appliedWeaponLocalRot = _equippedWeaponInstance.transform.localRotation;
            _hasAppliedWeaponPose = true;
#endif

            var colliders = _equippedWeaponInstance.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            var animators = _equippedWeaponInstance.GetComponentsInChildren<Animator>();
            for (int i = 0; i < animators.Length; i++)
            {
                animators[i].enabled = false;
            }

            _equippedWeaponInstance.layer = 2;

            _currentWeaponIsRifle = (gripProfile.Socket == Killtime.Core.Inventory.WeaponGripSocket.ChestTwoHands) || isRifle;
            _currentWeaponIsHeavyMelee = !_currentWeaponIsRifle && IsHeavyMeleeWeapon(item);
            if (_animator != null)
            {
                _animator.SetInteger(AnimWeaponType, GetWeaponType(item));
                if (_currentWeaponIsRifle && _isInCombat)
                {
                    _animator.CrossFadeInFixedTime(RifleIdleStateName, 0.1f);
                }
                else if (_currentWeaponIsHeavyMelee && _isInCombat)
                {
                    _animator.CrossFadeInFixedTime(WeaponFightIdleStateName, 0.1f);
                }
            }
        }

        public static bool IsPistolWeapon(Core.Inventory.InventoryItem item)
        {
            if (item == null || item.Type != Core.Inventory.ItemType.Weapon) return false;
            if (IsRifleWeapon(item) || IsMeleeWeapon(item)) return false;
            if (item.IsGrenade || item.IsThrowableGrenade() || item.IsLauncher) return false;
            return item.AssociatedSkill == SkillType.Ballistique;
        }

        /// <summary>
        /// Longueur monde cible (m) par famille d'arme, à _unitScale 1.
        /// </summary>
        private float GetTargetWeaponWorldLength(Core.Inventory.InventoryItem item)
        {
            if (item != null && (item.IsGrenade || item.IsThrowableGrenade())) return 0.22f;
            string kind = item != null ? (item.PlaceholderKind ?? "") : "";
            if (IsRifleWeapon(item))
            {
                if (kind == "SniperLaser") return 1.10f;
                if (kind == "Deglazer") return 1.00f;
                if (kind == "GrenadeLauncher") return 0.90f;
                if (kind == "Bow") return 1.25f;
                return 0.85f;
            }
            if (IsPistolWeapon(item)) return 0.32f;
            if (IsMeleeWeapon(item))
            {
                switch (kind)
                {
                    case "SwordMetalLong":
                    case "LaserSwordLong": return 1.35f;
                    case "Spear": return 1.80f;
                    case "Bow": return 1.25f;
                    case "Hammer": return 1.10f;
                    case "Axe": return 0.95f;
                    case "Club": return 0.75f;
                    case "SwordMetal":
                    case "LaserSword": return 1.00f;
                    default: return 1.00f;
                }
            }
            return 0.85f;
        }

        /// <summary>
        /// Mesure la plus grande dimension LOCALE (espace racine d'arme, sans le porteur)
        /// + son axe dominant (0=X, 1=Y, 2=Z). Les prefabs réels SciFi sont X-long,
        /// les placeholders fusils sont Z-long, les lames Y-long.
        /// </summary>
        private static float MeasureWeaponLocalLength(GameObject weaponRoot, out int longAxis)
        {
            longAxis = 2;
            if (weaponRoot == null) return 0f;
            bool has = false;
            Bounds b = default;
            var filters = weaponRoot.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                // Matrice locale de ce mesh relative à la racine d'arme (sans le porteur).
                Matrix4x4 m = Matrix4x4.TRS(mf.transform.localPosition, mf.transform.localRotation, mf.transform.localScale);
                Transform p = mf.transform.parent;
                while (p != null && p != weaponRoot.transform)
                {
                    m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
                    p = p.parent;
                }
                Bounds wb = TransformLocalBounds(mf.sharedMesh.bounds, m);
                if (!IsValidWeaponBounds(wb)) continue;
                if (!has) { b = wb; has = true; }
                else b.Encapsulate(wb);
            }
            var skinned = weaponRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < skinned.Length; i++)
            {
                var smr = skinned[i];
                if (smr == null || smr.sharedMesh == null) continue;
                Matrix4x4 m = Matrix4x4.TRS(smr.transform.localPosition, smr.transform.localRotation, smr.transform.localScale);
                Transform p = smr.transform.parent;
                while (p != null && p != weaponRoot.transform)
                {
                    m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
                    p = p.parent;
                }
                Bounds wb = TransformLocalBounds(smr.sharedMesh.bounds, m);
                if (!IsValidWeaponBounds(wb)) continue;
                if (!has) { b = wb; has = true; }
                else b.Encapsulate(wb);
            }
            if (!has) return 0f;
            Vector3 s = b.size;
            // Inclut l'échelle racine auteur (souvent 1) : on la retire pour avoir le "local brut".
            // En pratique racine = 1 ici, on garde simple et on mesure avec l'échelle courante.
            Vector3 rootScale = weaponRoot.transform.localScale;
            float rx = Mathf.Abs(rootScale.x) > 1e-6f ? s.x / Mathf.Abs(rootScale.x) : s.x;
            float ry = Mathf.Abs(rootScale.y) > 1e-6f ? s.y / Mathf.Abs(rootScale.y) : s.y;
            float rz = Mathf.Abs(rootScale.z) > 1e-6f ? s.z / Mathf.Abs(rootScale.z) : s.z;
            // Axe dominant sur les dimensions brutes.
            if (rx >= ry && rx >= rz) longAxis = 0;
            else if (ry >= rx && ry >= rz) longAxis = 1;
            else longAxis = 2;
            // Longueur locale brutes (sans échelle racine) : base du calcul monde.
            float rawMax = Mathf.Max(rx, Mathf.Max(ry, rz));
            if (float.IsNaN(rawMax) || float.IsInfinity(rawMax) || rawMax <= 1e-5f) return 0f;
            // Retourne la longueur AVEC l'échelle racine courante pour le facteur correctif.
            float withRoot = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            return withRoot <= 1e-6f ? 0f : withRoot;
        }

        private static Bounds TransformLocalBounds(Bounds local, Matrix4x4 m)
        {
            Vector3 center = m.MultiplyPoint3x4(local.center);
            Vector3 ext = local.extents;
            Vector3 axisX = m.MultiplyVector(new Vector3(ext.x, 0f, 0f));
            Vector3 axisY = m.MultiplyVector(new Vector3(0f, ext.y, 0f));
            Vector3 axisZ = m.MultiplyVector(new Vector3(0f, 0f, ext.z));
            Vector3 worldExt = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, worldExt * 2f);
        }

        private static bool IsValidWeaponBounds(Bounds b)
        {
            Vector3 s = b.size;
            return !(float.IsNaN(s.x) || float.IsNaN(s.y) || float.IsNaN(s.z)
                || float.IsInfinity(s.x) || float.IsInfinity(s.y) || float.IsInfinity(s.z))
                && s.x >= 0f && s.y >= 0f && s.z >= 0f
                && (s.x + s.y + s.z) > 1e-7f;
        }

        private static float GetParentUniformScale(Transform parent)
        {
            if (parent == null) return 1f;
            Vector3 ws = CharacterModelScaleNormalizer.ExtractWorldScale(parent);
            float avg = (Mathf.Abs(ws.x) + Mathf.Abs(ws.y) + Mathf.Abs(ws.z)) / 3f;
            if (float.IsNaN(avg) || float.IsInfinity(avg) || avg < 1e-4f) return 1f;
            return avg;
        }

        /// <summary>
        /// Ramène l'arme à sa longueur monde cible (x _unitScale), en compensant
        /// l'échelle du porteur (Soldier normalisé ~0.18, Mina ~1...). Facteur clampé :
        /// on ne rapetisse/grandit jamais de façon extrême sur une mesure douteuse.
        /// </summary>
        private void NormalizeWeaponWorldSize(GameObject weaponInstance, Core.Inventory.InventoryItem item, Killtime.Core.Inventory.WeaponGripProfile profile = null)
        {
            if (weaponInstance == null || item == null) return;
            float localLen = MeasureWeaponLocalLength(weaponInstance, out _);
            if (localLen <= 1e-5f) return;
            Transform parent = weaponInstance.transform.parent;
            float parentScale = GetParentUniformScale(parent);
            float measuredWorld = localLen * parentScale;

            float target = (profile != null && profile.TargetWorldLength > 0.05f)
                ? profile.TargetWorldLength
                : GetTargetWeaponWorldLength(item);
            target *= Mathf.Max(_unitScale, 1e-3f);

            float scaleMult = (profile != null && profile.ScaleMultiplier > 0.01f) ? profile.ScaleMultiplier : 1.0f;
            target *= scaleMult;

            if (target <= 1e-4f) return;
            float factor = target / Mathf.Max(measuredWorld, 1e-6f);
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f) return;

            if (factor < 0.10f || factor > 8f)
            {
                Vector3 authored = weaponInstance.transform.localScale;
                CharacterModelScaleNormalizer.SetWorldScale(weaponInstance.transform, authored * _unitScale * scaleMult);
                return;
            }
            weaponInstance.transform.localScale = weaponInstance.transform.localScale * factor;
        }
    }
}
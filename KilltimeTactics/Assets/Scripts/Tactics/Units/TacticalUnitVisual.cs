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
    public enum UnitAnimState
    {
        StandingIdle,
        StandingIdleToFightIdle,
        FightIdle,
        WeaponFightIdle,
        FightIdleToStandingIdle,
        ActionIdleToFightIdle,
        ActionIdleToStandingIdle,
        Walking,
        Roundkick,
        BodyBlock,
        FallingBackDeath,
        MeleeAttack
    }

    /// <summary>
    /// Gestionnaire visuel 3D et machine d'états d'animation pour une unité tactique.
    /// Orchestre les postures de combat, les transitions de garde, les flashs d'impact et les badges de statut.
    /// </summary>
    [RequireComponent(typeof(TacticalUnit))]
    public class TacticalUnitVisual : MonoBehaviour
    {
        [Header("Équipe & Thème")]
        [SerializeField] private Color _bodyColor = new Color(0.2f, 0.45f, 0.9f);
        [SerializeField] private Color _visorColor = new Color(0.0f, 0.9f, 1.0f);
        [SerializeField] private float _unitScale = 1.0f;

        [Header("Ancrage de l'Arme en Main")]
        [SerializeField] private Vector3 _weaponPositionOffset = Vector3.zero;
        [SerializeField] private Vector3 _weaponRotationOffset = new Vector3(-75f, 180f, 0f);

        [Header("Ancrage Fusil (2 mains) — poigne centrée, canon vers l'avant")]
        [Tooltip("Décalage MONDE (m) depuis la poitrine : X droite, Y haut, Z avant personnage. Converti en local en compensant l'échelle du porteur.")]
        [SerializeField] private Vector3 _riflePositionOffset = new Vector3(0.14f, 0.02f, 0.30f);
        [Tooltip("Correctif d'assiette pour les fusils Z-long (placeholders). Les prefabs réels X-long reçoivent un alignement auto +X -> +Z.")]
        [SerializeField] private Vector3 _rifleRotationOffset = new Vector3(-6f, 0f, 0f);

        [Header("Ancrage Pistolet (1 main) — canon vers l'avant")]
        [SerializeField] private Vector3 _pistolPositionOffset = Vector3.zero;
        [SerializeField] private Vector3 _pistolRotationOffset = new Vector3(-12f, 0f, 0f);

        [Header("HUD — Transparence Vision")]
        [Tooltip("Alpha appliqué aux icônes/HUD quand cet avatar est plus loin qu'un autre dans la vision (masqué).")]
        [SerializeField, Range(0.05f, 1f)] private float _occludedIconAlpha = 0.25f;
        [Tooltip("Rayon écran (px) dans lequel un avatar plus proche masque les icônes d'un avatar lointain.")]
        [SerializeField] private float _occlusionScreenRadiusPx = 110f;
        [Tooltip("Marge de profondeur (m) exigée pour considérer qu'un avatar est devant un autre.")]
        [SerializeField] private float _occlusionDepthBias = 0.6f;
        [Tooltip("Rayon corporel (m) utilisé pour le test volumétrique 3D. Quand la caméra est collée à un avatar, sa tête sort de l'écran : seul ce test rayon/segment détecte encore qu'il bouche la vue.")]
        [SerializeField] private float _occlusionBodyRadius = 0.55f;

        private TacticalUnit _unit;
        private Transform _modelRoot;
        private MeshRenderer _bodyRenderer;
        private MeshRenderer _teamDiskRenderer;
        private readonly List<Renderer> _customModelRenderers = new();
        private MaterialPropertyBlock _propBlock;
        private bool _isCustomModel = false;

        private Animator _animator;
        private AnimatorOverrideController _animatorOverride;
        private bool _isInCombat = true;
        private bool _wasMoving = false;
        private bool _isActionPlaying = false;
        private Coroutine _actionRoutine;

        private GameObject _equippedWeaponInstance;
        private string _lastEquippedItemId;
        private bool _currentWeaponIsRifle;
        private bool _currentWeaponIsHeavyMelee;
        private Coroutine _putAwayRoutine;
        /// <summary>
        /// Garde anti-doublon : vrai une fois les objets tenus lâchés au sol
        /// lors du passage à l'inconscience (réarmé à la reprise de conscience).
        /// </summary>
        private bool _heldItemsDroppedOnKO;
        // Dernière pose locale appliquée à l'arme (pour ne pas écraser la poigne fusil/pistolet
        // avec l'offset d'épée dans le bloc #if UNITY_EDITOR de Update).
#if UNITY_EDITOR
        private Vector3 _appliedWeaponLocalPos = Vector3.zero;
        private Quaternion _appliedWeaponLocalRot = Quaternion.identity;
        private bool _hasAppliedWeaponPose;
#endif

        private static readonly int AnimIsInCombat = Animator.StringToHash("IsInCombat");
        private static readonly int AnimTriggerAction = Animator.StringToHash("TriggerAction");
        private static readonly int AnimIsMoving = Animator.StringToHash("IsMoving");
        private static readonly int AnimTriggerRoundkick = Animator.StringToHash("TriggerRoundkick");
        private static readonly int AnimTriggerMeleeAttack = Animator.StringToHash("TriggerMeleeAttack");
        private static readonly int AnimTriggerFiringRifle = Animator.StringToHash("TriggerFiringRifle");
        private static readonly int AnimTriggerBodyBlock = Animator.StringToHash("TriggerBodyBlock");
        private static readonly int AnimTriggerFallingBackDeath = Animator.StringToHash("TriggerFallingBackDeath");
        private static readonly int AnimIsKO = Animator.StringToHash("IsKO");
        private static readonly int AnimWalkSpeedMultiplier = Animator.StringToHash("WalkSpeedMultiplier");
        private static readonly int AnimWeaponType = Animator.StringToHash("WeaponType");

        private bool _hasWalkingClip = false;
        private bool _hasWalkMultiplierParam = false;
        private float _naturalWalkSpeed = 1.35f;
        private bool _isUnconscious = false;

        private static readonly Dictionary<string, AnimationClip> _clipCache = new(StringComparer.OrdinalIgnoreCase);

        private readonly List<FloatingText> _floatingTexts = new();
        private float _hitFlashTimer = 0f;
        private Color _currentBodyColor;

        private static readonly Dictionary<StatusEffect, Texture2D> _statusIconCache = new();
        private StatusVisualInfo? _hoveredStatusInfo;
        private static readonly StatusEffect[] _allStatusEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));

        public Animator AnimatorComponent => _animator;
        public bool IsInCombat => _isInCombat;

        private struct StatusVisualInfo
        {
            public StatusEffect Status;
            public string Tag;
            public string Name;
            public string Description;
            public Color PrimaryColor;
        }

        private class FloatingText
        {
            public string Text;
            public Color Color;
            public Vector3 WorldPos;
            public float Lifetime;
            public float MaxLifetime;
        }

        private void Awake()
        {
            _unit = GetComponent<TacticalUnit>();
            _propBlock = new MaterialPropertyBlock();

            string modelName = _unit != null && _unit.Sheet != null ? _unit.Sheet.ModelPrefabName : null;
            if (!string.IsNullOrEmpty(modelName))
            {
                ApplyCustomModel(modelName);
            }
            else
            {
                BuildProceduralAvatar();
            }
        }

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

        public Transform GetSocketTransform(Killtime.Core.Inventory.WeaponGripSocket socketType)
        {
            return socketType switch
            {
                Killtime.Core.Inventory.WeaponGripSocket.LeftHand => GetLeftHandSocket(),
                Killtime.Core.Inventory.WeaponGripSocket.ChestTwoHands => GetRifleSocket(),
                Killtime.Core.Inventory.WeaponGripSocket.Back => GetBackSocket(),
                Killtime.Core.Inventory.WeaponGripSocket.Holster => GetHolsterSocket(),
                _ => GetRightHandSocket()
            };
        }

        private Transform GetLeftHandSocket()
        {
            if (_animator != null && _animator.isHuman)
            {
                var bone = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (bone != null) return bone;
            }

            Transform found = FindBoneRecursive(transform, "LeftHand", "Left_Hand", "Hand.L", "hand.l", "mixamorig:LeftHand", "weapon_l", "Bip001 L Hand");
            if (found != null) return found;

            if (_modelRoot != null) return _modelRoot;
            return transform;
        }

        private Transform GetBackSocket()
        {
            if (_animator != null && _animator.isHuman)
            {
                var upperChest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (upperChest != null) return upperChest;
                var chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest != null) return chest;
            }
            Transform found = FindBoneRecursive(transform, "Spine2", "Spine1", "mixamorig:Spine2", "mixamorig:Spine1", "Chest", "UpperChest");
            if (found != null) return found;
            if (_modelRoot != null) return _modelRoot;
            return transform;
        }

        private Transform GetHolsterSocket()
        {
            if (_animator != null && _animator.isHuman)
            {
                var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) return hips;
            }
            Transform found = FindBoneRecursive(transform, "Hips", "mixamorig:Hips", "Pelvis", "Bip001 Pelvis");
            if (found != null) return found;
            if (_modelRoot != null) return _modelRoot;
            return transform;
        }

        /// <summary>
        /// Rotation locale de poigne pour fusil/pistolet : canon à l'horizontale vers
        /// l'avant de l'unité, poignée dans la paume droite. On calcule le monde désiré
        /// (avant personnage) puis on repasse en local main, au lieu d'un Euler fixe
        /// qui dépend de l'orientation de l'os (cause de la diagonale sur Soldier).
        /// Prefabs réels X-long : alignement +X -> +Z. Placeholders Z-long : identité.
        /// </summary>
        private Quaternion ComputeFirearmGripRotation(GameObject weaponInstance, Transform socket, bool isRifle, Core.Inventory.InventoryItem item)
        {
            Vector3 fallbackEuler = isRifle ? _rifleRotationOffset : _pistolRotationOffset;
            try
            {
                int axis = 2;
                // Mesure sans l'échelle racine pour l'axe dominant.
                MeasureWeaponLocalLength(weaponInstance, out axis);
                Quaternion align = Quaternion.identity;
                if (axis == 0) align = Quaternion.Euler(0f, -90f, 0f); // canon +X -> avant +Z
                else if (axis == 1) align = Quaternion.Euler(90f, 0f, 0f); // canon +Y -> avant +Z

                Quaternion charWorld = transform != null ? transform.rotation : Quaternion.identity;
                // Légère assiette vers le haut pour une visée naturelle (12° paramétrable).
                Quaternion tilt = Quaternion.Euler(fallbackEuler);
                Quaternion desiredWorld = charWorld * tilt * align;

                Quaternion handWorld = socket != null ? socket.rotation : charWorld;
                // Sécurité : quaternion invalide -> repli Euler.
                if (handWorld.x * handWorld.x + handWorld.y * handWorld.y + handWorld.z * handWorld.z + handWorld.w * handWorld.w < 1e-6f)
                    return Quaternion.Euler(fallbackEuler) * align;
                Quaternion local = Quaternion.Inverse(handWorld) * desiredWorld;
                if (float.IsNaN(local.x + local.y + local.z + local.w) || float.IsInfinity(local.x + local.y + local.z + local.w))
                    return Quaternion.Euler(fallbackEuler) * align;
                return local;
            }
            catch
            {
                return Quaternion.Euler(fallbackEuler);
            }
        }

        /// <summary>
        /// Poigne fusil sur la poitrine : le torse ne tourne presque pas entre T-pose et
        /// "Rifle Idle" (contrairement à la main), donc un alignement fixe +X->+Z / +Y->+Z
        /// + assiette donne un canon vers l'avant stable sur tous les rigs.
        /// On repasse quand même par le monde pour absorber une éventuelle rotation de torse.
        /// </summary>
        private Quaternion ComputeRifleChestGripRotation(GameObject weaponInstance, Transform socket, Core.Inventory.InventoryItem item)
        {
            try
            {
                int axis = 2;
                MeasureWeaponLocalLength(weaponInstance, out axis);
                Quaternion align = Quaternion.identity;
                if (axis == 0) align = Quaternion.Euler(0f, -90f, 0f); // SciFi réel : canon +X -> avant +Z
                else if (axis == 1) align = Quaternion.Euler(90f, 0f, 0f); // lame Y -> avant +Z (jamais pour un fusil)

                Quaternion charWorld = transform != null ? transform.rotation : Quaternion.identity;
                Quaternion tilt = Quaternion.Euler(_rifleRotationOffset);
                Quaternion desiredWorld = charWorld * tilt * align;

                Quaternion chestWorld = socket != null ? socket.rotation : charWorld;
                if (chestWorld.x * chestWorld.x + chestWorld.y * chestWorld.y + chestWorld.z * chestWorld.z + chestWorld.w * chestWorld.w < 1e-6f)
                    return tilt * align;
                Quaternion local = Quaternion.Inverse(chestWorld) * desiredWorld;
                if (float.IsNaN(local.x + local.y + local.z + local.w) || float.IsInfinity(local.x + local.y + local.z + local.w))
                    return tilt * align;
                return local;
            }
            catch
            {
                return Quaternion.Euler(_rifleRotationOffset);
            }
        }

        private void StartPutAwayWeapon()
        {
            if (_putAwayRoutine != null)
            {
                StopCoroutine(_putAwayRoutine);
            }
            _putAwayRoutine = StartCoroutine(PutAwayWeaponRoutine());
        }

        private IEnumerator PutAwayWeaponRoutine()
        {
            if (_animator != null)
            {
                _animator.SetInteger(AnimWeaponType, 0);
                _animator.CrossFadeInFixedTime("Rifle Put Away", 0.08f);
            }

            yield return new WaitForSeconds(0.65f);

            ClearEquippedWeaponInstance();
            _putAwayRoutine = null;
        }

        private void ClearEquippedWeaponInstance()
        {
            if (_equippedWeaponInstance != null)
            {
                Destroy(_equippedWeaponInstance);
                _equippedWeaponInstance = null;
            }
#if UNITY_EDITOR
            _hasAppliedWeaponPose = false;
#endif
        }

        private static void DisableWeaponPhysics(GameObject root)
        {
            if (root == null) return;
            var cols = root.GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            var anims = root.GetComponentsInChildren<Animator>();
            for (int i = 0; i < anims.Length; i++) anims[i].enabled = false;
        }

        /// <summary>
        /// Règle Livre VII : à l'inconscience / mort, la main s'ouvre et l'arme
        /// (ou tout objet tenu) se décroche et tombe au sol en <b>physique réelle</b>
        /// (Rigidbody : gravité, rebond, friction — voir <see cref="DroppedWeaponPickup"/>).
        /// L'item est retiré de l'inventaire du porteur : l'objet au sol devient
        /// l'exemplaire unique, ramassable par une unité consciente à portée.
        /// Idempotent par K.O. (garde <see cref="_heldItemsDroppedOnKO"/>).
        /// Retourne true si au moins une arme a été lâchée.
        /// </summary>
        public bool DropHeldItemsWithPhysics()
        {
            if (_heldItemsDroppedOnKO) return false;
            if (_unit == null) return false;
            var sheet = _unit.GetOrBuildSheet();
            if (sheet == null) return false;

            // Retire les armes équipées de l'inventaire (exemplaire unique -> sol).
            var droppedItems = sheet.ExtractEquippedItemsForGroundDrop();
            GameObject visualInstance = _equippedWeaponInstance;

            if ((droppedItems == null || droppedItems.Count == 0) && visualInstance == null)
                return false;

            // Cas désync (visuel sans fiche) : on lâche quand même le visuel
            // avec un item reconstitué minimal pour ne pas le faire disparaître.
            if ((droppedItems == null || droppedItems.Count == 0) && visualInstance != null)
            {
                // Rien en fiche : on détruit proprement le visuel orphelin sans spawn.
                // (Évite de dupliquer une arme déjà lootée / vendue.)
                _equippedWeaponInstance = null;
                _lastEquippedItemId = null;
                _currentWeaponIsRifle = false;
                _currentWeaponIsHeavyMelee = false;
                if (_animator != null) _animator.SetInteger(AnimWeaponType, 0);
                Destroy(visualInstance);
                _heldItemsDroppedOnKO = true;
                return false;
            }

            // Détache le visuel AVANT tout refresh pour ne pas le détruire.
            _equippedWeaponInstance = null;
            _lastEquippedItemId = null;
            _currentWeaponIsRifle = false;
            _currentWeaponIsHeavyMelee = false;
            if (_putAwayRoutine != null)
            {
                try { StopCoroutine(_putAwayRoutine); } catch { }
                _putAwayRoutine = null;
            }
            if (_animator != null) _animator.SetInteger(AnimWeaponType, 0);
#if UNITY_EDITOR
            _hasAppliedWeaponPose = false;
#endif

            bool anyDropped = false;
            for (int i = 0; i < droppedItems.Count; i++)
            {
                var item = droppedItems[i];
                if (item == null) continue;

                GameObject groundGO = null;
                if (i == 0 && visualInstance != null)
                {
                    groundGO = visualInstance; // réutilise le prefab / placeholder en main.
                }
                else
                {
                    // Arme secondaire / sans visuel : construit un placeholder au sol.
                    try
                    {
                        groundGO = Killtime.Core.Inventory.ArmoryPlaceholderFactory.ResolveOrBuild(item, null);
                    }
                    catch { groundGO = null; }
                    if (groundGO == null)
                    {
                        groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        groundGO.transform.localScale = new Vector3(0.08f, 0.08f, 0.7f);
                    }
                    // Pose initiale : à la main droite, hauteur poitrine.
                    Vector3 handPos = transform.position + Vector3.up * 1.25f * _unitScale + transform.forward * 0.25f;
                    groundGO.transform.position = handPos;
                    groundGO.transform.rotation = transform.rotation;
                }

                if (groundGO == null) continue;

                // Détachement monde-réel : conserve pose/monde, quitte la main.
                try { groundGO.transform.SetParent(null, true); }
                catch { groundGO.transform.parent = null; }
                groundGO.name = "Dropped_" + item.Name;

                // Impulsion d'ouverture de main : la main K.O. ne lance pas,
                // elle relâche — petit poussé avant + latéral aléatoire + rotation.
                Vector3 fwd = transform.forward;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                float massRef = Mathf.Clamp(item.WeightKg > 0.01f ? item.WeightKg : 1.2f, 0.3f, 10f);
                Vector3 vel = fwd * UnityEngine.Random.Range(0.4f, 0.9f)
                    + Vector3.up * UnityEngine.Random.Range(0.1f, 0.45f)
                    + side * UnityEngine.Random.Range(-0.5f, 0.5f);
                // Les armes lourdes partent moins vite (inertie) : vitesse ~ impulsion / masse.
                vel *= Mathf.Clamp(1.4f / massRef, 0.5f, 1.2f);
                Vector3 angVel = new Vector3(
                    UnityEngine.Random.Range(-5f, 5f),
                    UnityEngine.Random.Range(-4f, 4f),
                    UnityEngine.Random.Range(-6f, 6f));

                var pickup = groundGO.GetComponent<DroppedWeaponPickup>();
                if (pickup == null) pickup = groundGO.AddComponent<DroppedWeaponPickup>();
                string ownerName = (_unit.Stats != null && !string.IsNullOrEmpty(_unit.Stats.Name))
                    ? _unit.Stats.Name
                    : _unit.name;
                pickup.PickupRadius = 1.2f;
                pickup.Initialize(item, ownerName, vel, angVel);
                anyDropped = true;
            }

            if (anyDropped)
            {
                _heldItemsDroppedOnKO = true;
                try
                {
                    var audio = Killtime.Audio.KilltimeAudioManager.Instance;
                    if (audio != null)
                        audio.PlayAt(Killtime.Audio.SoundId.Grenade_Bounce, transform.position + Vector3.up * 0.8f, 0.7f);
                }
                catch { }
                try { SpawnFloatingText("⚔ Arme lâchée", new Color(1f, 0.75f, 0.3f)); } catch { }
                // Sauvegarde l'inventaire sans arme (le visuel est déjà détaché, pas de re-attach).
                try
                {
                    if (sheet != null)
                        Killtime.Core.Character.CharacterStorageService.SaveCharacter(sheet);
                }
                catch { }
            }
            else
            {
                _heldItemsDroppedOnKO = true;
            }

            return anyDropped;
        }

        private Transform GetRightHandSocket()
        {
            if (_animator != null && _animator.isHuman)
            {
                var bone = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (bone != null) return bone;
            }

            Transform found = FindBoneRecursive(transform, "RightHand", "Right_Hand", "Hand.R", "hand.r", "mixamorig:RightHand", "weapon_r", "Bip001 R Hand");
            if (found != null) return found;

            if (_modelRoot != null) return _modelRoot;
            return transform;
        }

        /// <summary>
        /// Ancrage fusil 2M : poitrine/épine pour une poigne à deux mains centrée.
        /// Stable entre les rigs (Mixamo Soldier vs Blender Mina) contrairement à la main,
        /// dont l'orientation varie et donnait la diagonale flottante sur Soldier.
        /// </summary>
        private Transform GetRifleSocket()
        {
            if (_animator != null && _animator.isHuman)
            {
                var chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest != null) return chest;
                var spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
                if (spine != null) return spine;
                var neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
                if (neck != null) return neck;
            }
            Transform found = FindBoneRecursive(transform,
                "Chest", "Spine2", "Spine1", "Spine", "mixamorig:Spine2", "mixamorig:Spine1",
                "mixamorig:Spine", "Neck", "mixamorig:Neck", "Thorax", "UpperChest");
            if (found != null) return found;
            if (_modelRoot != null) return _modelRoot;
            return transform;
        }

        private static Transform FindBoneRecursive(Transform parent, params string[] names)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                for (int j = 0; j < names.Length; j++)
                {
                    if (child.name.IndexOf(names[j], StringComparison.OrdinalIgnoreCase) >= 0)
                        return child;
                }
                var deep = FindBoneRecursive(child, names);
                if (deep != null) return deep;
            }
            return null;
        }

        private static GameObject LoadWeaponPrefab(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string clean = path;
            if (clean.StartsWith("Guns/", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(5);

            var loaded = Resources.Load<GameObject>($"Guns/{clean}")
                      ?? Resources.Load<GameObject>(clean)
                      ?? Resources.Load<GameObject>($"Objects/{clean}")
                      ?? Resources.Load<GameObject>(path);

#if UNITY_EDITOR
            if (loaded == null)
            {
                string fName = System.IO.Path.GetFileName(clean);
                string[] guids = UnityEditor.AssetDatabase.FindAssets($"{fName} t:Prefab", new[] { "Assets/Resources/Guns", "Assets/Resources/Objects" });
                if (guids != null && guids.Length > 0)
                {
                    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
                }
            }
#endif
            return loaded;
        }

        private void Start()
        {
            Killtime.Core.Inventory.WeaponGripService.OnProfilesChanged -= HandleWeaponGripProfilesChanged;
            Killtime.Core.Inventory.WeaponGripService.OnProfilesChanged += HandleWeaponGripProfilesChanged;

            if (_animator != null && _isInCombat)
            {
                _animator.SetBool(AnimIsInCombat, true);
                _animator.SetInteger(AnimWeaponType, GetCurrentWeaponType());
                _animator.Play(GetCurrentCombatIdleStateName(), 0, 0f);
            }
        }

        private void HandleWeaponGripProfilesChanged()
        {
            _lastEquippedItemId = null;
            UpdateEquippedWeaponVisual();
        }

        private void Update()
        {
            if (Time.timeScale <= 0.0001f) return;

            UpdateEquippedWeaponVisual();
            UpdateAnimatorParameters();
            UpdateFloatingTexts();
            UpdateHitFlash();
            UpdateKOAnimation();

#if UNITY_EDITOR
            if (_equippedWeaponInstance != null && _hasAppliedWeaponPose)
            {
                // Rejoue la pose calculée (poigne fusil/pistolet vs épée) pour le live-tuning,
                // sans écraser la poigne fusil avec l'offset d'épée.
                _equippedWeaponInstance.transform.localPosition = _appliedWeaponLocalPos;
                _equippedWeaponInstance.transform.localRotation = _appliedWeaponLocalRot;
            }
#endif
        }

        private void OnDestroy()
        {
            Killtime.Core.Inventory.WeaponGripService.OnProfilesChanged -= HandleWeaponGripProfilesChanged;

            if (_putAwayRoutine != null)
            {
                StopCoroutine(_putAwayRoutine);
                _putAwayRoutine = null;
            }
            ClearEquippedWeaponInstance();
        }

        private void UpdateAnimatorParameters()
        {
            if (_animator == null) return;

            bool moving = _unit != null && _unit.IsMoving;
            _animator.SetBool(AnimIsMoving, moving);

            bool alive = _unit != null && _unit.Stats != null && _unit.Stats.IsAlive;
            _animator.SetBool(AnimIsInCombat, _isInCombat && alive);

            bool hasRifle = HasRifleEquipped();
            bool hasHeavy = !hasRifle && HasHeavyMeleeEquipped();
            _animator.SetInteger(AnimWeaponType, GetCurrentWeaponType());

            if (_hasWalkingClip && _hasWalkMultiplierParam && _unit != null)
            {
                float multiplier = moving ? Mathf.Max(0.1f, _unit.MoveSpeed / _naturalWalkSpeed) : 1.0f;
                _animator.SetFloat(AnimWalkSpeedMultiplier, multiplier);
            }

            if (moving != _wasMoving)
            {
                _wasMoving = moving;
                if (moving)
                {
                    string walkState = hasRifle ? "Rifle Walk To Stop" : "Walking";
                    _animator.CrossFadeInFixedTime(walkState, 0.1f);
                }
                else
                {
                    string idleState;
                    if (hasRifle) idleState = RifleIdleStateName;
                    else if (hasHeavy && _isInCombat && alive) idleState = WeaponFightIdleStateName;
                    else if (_isInCombat && alive) idleState = FightIdleStateName;
                    else idleState = "Standing Idle";
                    _animator.CrossFadeInFixedTime(idleState, 0.15f);
                }
            }
        }

        public void SetCombatStance(bool inCombat)
        {
            _isInCombat = inCombat;
            if (_animator != null)
            {
                _animator.SetBool(AnimIsInCombat, inCombat);
                _animator.SetInteger(AnimWeaponType, GetCurrentWeaponType());

                if (inCombat)
                {
                    _animator.CrossFadeInFixedTime(GetCurrentCombatIdleStateName(), 0.1f);
                }
                else
                {
                    _animator.CrossFadeInFixedTime("Standing Idle", 0.15f);
                }
            }
        }

        public void TriggerActionAnimation()
        {
            if (HasRifleEquipped())
            {
                TriggerFiringRifle();
            }
            else if (HasMeleeWeaponEquipped())
            {
                TriggerMeleeAttack();
            }
            else
            {
                TriggerRoundkick();
            }
        }

        public void TriggerRoundkick(bool forceKick = false)
        {
            if (HasRifleEquipped() && !forceKick)
            {
                TriggerFiringRifle();
                return;
            }

            // Arme de mêlée en main => coup d'arme ("Melee Attack"), pas coup de pied.
            // forceKick=true (attaque de contact forcée, ex. crosse de fusil) ne s'applique
            // qu'aux unités sans arme de mêlée : à mains nues ou au fusil on garde le Roundkick.
            if (HasMeleeWeaponEquipped())
            {
                TriggerMeleeAttack();
                return;
            }

            if (_isActionPlaying) return;

            _isInCombat = true;
            if (_animator != null)
            {
                _animator.SetBool(AnimIsInCombat, true);
                _animator.SetInteger(AnimWeaponType, 0);
                _animator.ResetTrigger(AnimTriggerAction);
                _animator.ResetTrigger(AnimTriggerRoundkick);
                _animator.ResetTrigger(AnimTriggerMeleeAttack);
                _animator.CrossFadeInFixedTime("Roundkick", 0.08f);

                if (_actionRoutine != null)
                {
                    StopCoroutine(_actionRoutine);
                }
                _actionRoutine = StartCoroutine(ActionLockRoutine());
            }
        }

        public void TriggerFiringRifle()
        {
            if (_isActionPlaying) return;

            _isInCombat = true;
            if (_animator != null)
            {
                _animator.SetBool(AnimIsInCombat, true);
                _animator.SetInteger(AnimWeaponType, WeaponTypeRifle);
                _animator.ResetTrigger(AnimTriggerAction);
                _animator.ResetTrigger(AnimTriggerFiringRifle);
                _animator.ResetTrigger(AnimTriggerMeleeAttack);
                _animator.CrossFadeInFixedTime("Firing Rifle", 0.05f);

                if (_actionRoutine != null)
                {
                    StopCoroutine(_actionRoutine);
                }
                _actionRoutine = StartCoroutine(ActionLockRoutine());
            }
        }

        /// <summary>
        /// Coup d'arme de mêlée (épée, hache, marteau, pique...) : état "Melee Attack"
        /// du PlayerAnimator (clip "Standing Melee Attack", one-shot non loopé).
        /// Lames légères => WeaponType 0 (Fight Idle), matraque/hache/marteau => 2 (Weapon Fight Idle).
        /// </summary>
        public void TriggerMeleeAttack()
        {
            if (_isActionPlaying) return;

            _isInCombat = true;
            if (_animator != null)
            {
                _animator.SetBool(AnimIsInCombat, true);
                _animator.SetInteger(AnimWeaponType, GetCurrentWeaponType());
                _animator.ResetTrigger(AnimTriggerAction);
                _animator.ResetTrigger(AnimTriggerRoundkick);
                _animator.ResetTrigger(AnimTriggerFiringRifle);
                _animator.ResetTrigger(AnimTriggerMeleeAttack);
                _animator.SetTrigger(AnimTriggerMeleeAttack);
                _animator.CrossFadeInFixedTime("Melee Attack", 0.08f);

                if (_actionRoutine != null)
                {
                    StopCoroutine(_actionRoutine);
                }
                _actionRoutine = StartCoroutine(ActionLockRoutine());
            }
        }

        /// <summary>
        /// Animation de lancer de grenade : lever de bras + projection.
        /// Si un lance-grenades est équipé visuellement (arme 2H/Ballistique),
        /// joue le tir épaulé ; sinon le geste de lancer à la main.
        /// Repli procédural (lean arrière) si aucun clip disponible.
        /// </summary>
        public void TriggerGrenadeThrow()
        {
            if (_isActionPlaying) return;
            _isInCombat = true;
            // Lanceur épaulé => même posture que le tir au fusil.
            bool hasLauncher = false;
            try
            {
                var sheet = _unit != null ? _unit.GetOrBuildSheet() : null;
                if (sheet != null && sheet.Inventory != null)
                {
                    for (int i = 0; i < sheet.Inventory.Count; i++)
                    {
                        var it = sheet.Inventory[i];
                        if (it != null && it.IsLauncher && it.IsEquipped) { hasLauncher = true; break; }
                    }
                }
            }
            catch { hasLauncher = false; }
            if (hasLauncher || HasRifleEquipped())
            {
                TriggerFiringRifle();
                return;
            }
            if (_animator != null)
            {
                _animator.SetBool(AnimIsInCombat, true);
                _animator.SetInteger(AnimWeaponType, GetCurrentWeaponType());
                _animator.ResetTrigger(AnimTriggerAction);
                _animator.ResetTrigger(AnimTriggerRoundkick);
                _animator.ResetTrigger(AnimTriggerMeleeAttack);
                _animator.CrossFadeInFixedTime("Roundkick", 0.08f);
                if (_actionRoutine != null) StopCoroutine(_actionRoutine);
                _actionRoutine = StartCoroutine(ActionLockRoutine());
            }
            else
            {
                // Repli procédural : pas d'Animator (avatar capsule) => on garde la garde.
                _isInCombat = true;
            }
        }

        private IEnumerator ActionLockRoutine()
        {
            _isActionPlaying = true;
            yield return new WaitForSeconds(0.85f);
            if (_animator != null)
            {
                _animator.ResetTrigger(AnimTriggerAction);
                _animator.ResetTrigger(AnimTriggerRoundkick);
                _animator.ResetTrigger(AnimTriggerFiringRifle);
                _animator.ResetTrigger(AnimTriggerMeleeAttack);
            }
            _isActionPlaying = false;
            _actionRoutine = null;
        }

        public void TriggerBodyBlock()
        {
            if (_unit != null && _unit.Stats != null && !_unit.Stats.CanDefendActively()) return;

            _isInCombat = true;
            if (_animator != null)
            {
                _animator.SetBool(AnimIsInCombat, true);
                _animator.ResetTrigger(AnimTriggerBodyBlock);
                _animator.SetTrigger(AnimTriggerBodyBlock);
                _animator.CrossFadeInFixedTime("Body Block", 0.08f);
            }
        }

        public void TriggerFallingBackDeath()
        {
            _isUnconscious = true;
            // La main s'ouvre immédiatement : l'arme tombe en physique réelle.
            try { DropHeldItemsWithPhysics(); } catch { }
            if (_animator != null)
            {
                _animator.SetBool(AnimIsKO, true);
                _animator.ResetTrigger(AnimTriggerFallingBackDeath);
                _animator.SetTrigger(AnimTriggerFallingBackDeath);
                _animator.CrossFadeInFixedTime("Falling Back Death", 0.12f);
            }
        }

        public void PlayAnimationState(UnitAnimState state, float crossFadeDuration = 0.15f)
        {
            if (_animator == null) return;

            string stateName = state switch
            {
                UnitAnimState.StandingIdle => "Standing Idle",
                UnitAnimState.StandingIdleToFightIdle => "Standing Idle To Fight Idle",
                UnitAnimState.FightIdle => "Fight Idle",
                UnitAnimState.WeaponFightIdle => "Weapon Fight Idle",
                UnitAnimState.FightIdleToStandingIdle => "Fight Idle To Standing Idle",
                UnitAnimState.ActionIdleToFightIdle => "Action Idle To Fight Idle",
                UnitAnimState.ActionIdleToStandingIdle => "Action Idle To Standing Idle",
                UnitAnimState.Walking => "Walking",
                UnitAnimState.Roundkick => "Roundkick",
                UnitAnimState.BodyBlock => "Body Block",
                UnitAnimState.FallingBackDeath => "Falling Back Death",
                UnitAnimState.MeleeAttack => "Melee Attack",
                _ => "Standing Idle"
            };

            _animator.CrossFadeInFixedTime(stateName, crossFadeDuration);
        }

        private void SetupAnimator(GameObject instance, GameObject sourcePrefab = null)
        {
            _animator = instance.GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                _animator = instance.AddComponent<Animator>();
            }

            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (_animator.avatar == null && sourcePrefab != null)
            {
                var srcAnim = sourcePrefab.GetComponentInChildren<Animator>();
                if (srcAnim != null && srcAnim.avatar != null)
                {
                    _animator.avatar = srcAnim.avatar;
                }
            }

            RuntimeAnimatorController baseController = _animator.runtimeAnimatorController;
            if (baseController == null)
            {
                baseController = Resources.Load<RuntimeAnimatorController>("PlayerAnimator")
                              ?? Resources.Load<RuntimeAnimatorController>("Animations/PlayerAnimator");
            }

            if (baseController != null)
            {
                ConfigureAnimatorClips(baseController);
            }

            if (_animator != null && _isInCombat)
            {
                _animator.SetBool(AnimIsInCombat, true);
                _animator.Play("Fight Idle", 0, 0f);
            }
        }

        private void ConfigureAnimatorClips(RuntimeAnimatorController baseController)
        {
            _animatorOverride = new AnimatorOverrideController(baseController);
            string genderKey = ResolveAnimationGenderKey();

            BindClipToOverride("Standing Idle", genderKey);
            BindClipToOverride("Standing Idle To Fight Idle", genderKey);
            BindClipToOverride("Fight Idle", genderKey);
            BindClipToOverride(WeaponFightIdleStateName, genderKey);
            BindClipToOverride("Fight Idle To Standing Idle", genderKey);
            BindClipToOverride("Walking", genderKey);
            BindClipToOverride("Roundkick", genderKey);
            // Attaque à l'arme de mêlée : l'état Animator s'appelle "Melee Attack"
            // mais le clip importé (Mixamo) s'appelle "Standing Melee Attack".
            BindClipToOverride("Melee Attack", genderKey);
            BindClipToOverride("Standing Melee Attack", genderKey);
            BindClipToOverride("Body Block", genderKey);
            BindClipToOverride("Falling Back Death", genderKey);
            BindClipToOverride("Rifle Idle", genderKey);
            BindClipToOverride("Rifle Walk To Stop", genderKey);
            // Alias legacy : l'état s'appelait "Walk With Rifle" dans le .controller,
            // le FBX normalisé s'appelle "Rifle Walk To Stop".
            BindClipToOverride("Walk With Rifle", genderKey);
            BindClipToOverride("Firing Rifle", genderKey);
            BindClipToOverride("Rifle Put Away", genderKey);

            _animator.runtimeAnimatorController = _animatorOverride;
            CheckWalkingAnimationCapabilities();
        }

        /// <summary>
        /// Re-applique la variante d'animations (Male/Female) sans toucher à l'arbre.
        /// Même PlayerAnimator.controller pour les deux sexes, seuls les clips changent
        /// via l'AnimatorOverrideController runtime. Base historique = Female.
        /// </summary>
        public void ReapplyAnimationVariant()
        {
            if (_animator == null || _animatorOverride == null) return;
            var baseController = _animatorOverride.runtimeAnimatorController;
            if (baseController == null) return;
            ConfigureAnimatorClips(baseController);
        }

        /// <summary>
        /// Détermine la variante à charger : "Male" ou "Female".
        /// Priorité : Sheet.Gender, puis nom du prefab (Mina/wife = Female, Soldier/hitman/boss = Male).
        /// Défaut = Female pour préserver le comportement actuel (clips existants).
        /// </summary>
        private string ResolveAnimationGenderKey()
        {
            string gender = _unit != null && _unit.Sheet != null ? _unit.Sheet.Gender : null;
            if (!string.IsNullOrEmpty(gender))
            {
                string g = gender.Trim().ToLowerInvariant();
                if (g.StartsWith("m") || g.Contains("masc") || g.Contains("male") || g.Contains("homme") || g.Contains("homem"))
                    return "Male";
                if (g.StartsWith("f") || g.Contains("fem") || g.Contains("fém") || g.Contains("woman") || g.Contains("girl"))
                    return "Female";
                // "Indéterminé" et autres -> fallback sur le modèle ci-dessous.
            }

            string modelName = _unit != null && _unit.Sheet != null ? _unit.Sheet.ModelPrefabName : null;
            if (!string.IsNullOrEmpty(modelName))
            {
                string m = modelName.ToLowerInvariant();
                if (m.Contains("mina") || m.Contains("wife") || m.Contains("female") || m.Contains("femelle") || m.Contains("woman") || m.Contains("girl"))
                    return "Female";
                if (m.Contains("soldier") || m.Contains("hitman") || m.Contains("boss") || m.Contains("male") || m.Contains("homme"))
                    return "Male";
            }

            return "Female";
        }

        private void CheckWalkingAnimationCapabilities()
        {
            _hasWalkingClip = false;
            _hasWalkMultiplierParam = false;
            _naturalWalkSpeed = 1.35f;

            if (_animator == null) return;

            for (int i = 0; i < _animator.parameterCount; i++)
            {
                if (_animator.parameters[i].nameHash == AnimWalkSpeedMultiplier)
                {
                    _hasWalkMultiplierParam = true;
                    break;
                }
            }

            AnimationClip walkingClip = null;
            if (_animatorOverride != null)
            {
                walkingClip = _animatorOverride["Walking"];
            }

            if (walkingClip == null)
            {
                walkingClip = LoadAnimationClip("Walking", ResolveAnimationGenderKey());
            }

            if (walkingClip != null)
            {
                _hasWalkingClip = true;
                float avgSpeed = walkingClip.averageSpeed.magnitude;
                if (avgSpeed > 0.1f)
                {
                    _naturalWalkSpeed = avgSpeed;
                }
            }
        }

        private void BindClipToOverride(string clipName, string genderKey = "Female")
        {
            AnimationClip clip = LoadAnimationClip(clipName, genderKey);
            if (clip != null && _animatorOverride != null)
            {
                _animatorOverride[clipName] = clip;
            }
        }

        private static AnimationClip LoadAnimationClip(string clipName)
        {
            return LoadAnimationClip(clipName, "Female");
        }

        private static AnimationClip LoadAnimationClip(string clipName, string genderKey)
        {
            string cacheKey = string.IsNullOrEmpty(genderKey) ? clipName : $"{genderKey}/{clipName}";
            if (_clipCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }
            // Variante genrée en priorité, fallback sur l'existant (Female historique).
            // Ordre : Animations/{Gender}/{clip} -> Animations/Female/{clip} -> Animations/{clip} -> racine.
            var candidatePaths = new List<string>();
            if (!string.IsNullOrEmpty(genderKey))
                candidatePaths.Add($"Animations/{genderKey}/{clipName}");
            if (!string.Equals(genderKey, "Female", StringComparison.OrdinalIgnoreCase))
                candidatePaths.Add($"Animations/Female/{clipName}");
            candidatePaths.Add($"Animations/{clipName}");
            candidatePaths.Add(clipName);

            // Cas spécial : "Melee Attack" (état) <-> "Standing Melee Attack" (clip Mixamo).
            var candidateNames = new List<string> { clipName };
            if (clipName == "Melee Attack")
                candidateNames.Add("Standing Melee Attack");
            if (clipName == "Walk With Rifle")
                candidateNames.Add("Rifle Walk To Stop");
            if (clipName == "Rifle Walk To Stop")
                candidateNames.Add("Walk With Rifle");

            AnimationClip clip = null;
            foreach (var path in candidatePaths)
            {
                var subClips = Resources.LoadAll<AnimationClip>(path);
                if (subClips != null && subClips.Length > 0)
                {
                    for (int i = 0; i < subClips.Length; i++)
                    {
                        if (subClips[i] != null && !subClips[i].name.StartsWith("__preview__"))
                        {
                            clip = subClips[i];
                            break;
                        }
                    }
                }
                if (clip != null) break;

                string dir = path.Contains("/") ? path.Substring(0, path.LastIndexOf('/')) : "";
                foreach (var altName in candidateNames)
                {
                    if (string.Equals(altName, clipName, StringComparison.OrdinalIgnoreCase)) continue;
                    string altPath = string.IsNullOrEmpty(dir) ? altName : $"{dir}/{altName}";
                    var altClips = Resources.LoadAll<AnimationClip>(altPath);
                    if (altClips != null)
                    {
                        for (int i = 0; i < altClips.Length; i++)
                        {
                            if (altClips[i] != null && !altClips[i].name.StartsWith("__preview__")
                                && string.Equals(altClips[i].name, altName, StringComparison.OrdinalIgnoreCase))
                            {
                                clip = altClips[i];
                                break;
                            }
                        }
                    }
                    if (clip != null) break;
                }
                if (clip != null) break;

                clip = Resources.Load<AnimationClip>(path);
                if (clip != null) break;
            }

            if (clip != null)
            {
                _clipCache[cacheKey] = clip;
            }

            return clip;
        }

        public void SetColor(Color bodyColor, Color visorColor)
        {
            _bodyColor = bodyColor;
            _visorColor = visorColor;
            _currentBodyColor = bodyColor;
            ApplyBodyColor(bodyColor);
        }

        private Material CreateSafeUnitMaterial(string matName)
        {
            Shader targetShader = null;

            var currentRP = GraphicsSettings.currentRenderPipeline;
            if (currentRP != null)
            {
                string rpName = currentRP.GetType().Name;
                if (rpName.Contains("Universal") || rpName.Contains("URP"))
                {
                    targetShader = Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                                ?? Shader.Find("Universal Render Pipeline/Unlit");
                }
                else if (rpName.Contains("HighDefinition") || rpName.Contains("HDRP"))
                {
                    targetShader = Shader.Find("HDRP/Lit")
                                ?? Shader.Find("HDRP/Unlit");
                }
            }

            if (targetShader == null)
            {
                targetShader = Shader.Find("Killtime/TacticalLit")
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");
            }

            return new Material(targetShader) { name = matName };
        }

        public bool ApplyCustomModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName) || modelName == "(Procédural)")
            {
                BuildProceduralAvatar();
                return true;
            }

            string cleanName = modelName.StartsWith("Characters/") ? modelName.Substring("Characters/".Length) : modelName;
            GameObject prefab = Resources.Load<GameObject>($"Characters/{cleanName}") ?? Resources.Load<GameObject>(cleanName);

            if (prefab == null)
            {
                Debug.LogWarning($"[TacticalUnitVisual] Modèle introuvable sous Resources/Characters/{cleanName}. Repli sur l'avatar procédural.");
                BuildProceduralAvatar();
                return false;
            }

            if (_modelRoot != null) Destroy(_modelRoot.gameObject);

            _modelRoot = new GameObject("AvatarModel").transform;
            _modelRoot.SetParent(transform, false);

            GameObject instance = Instantiate(prefab, _modelRoot);
            instance.name = cleanName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, Vector3.one * _unitScale);

            // Normalisation générique : ramène n'importe quel prefab (trop petit ou trop
            // grand) à la taille d'unité standard, sans valeur hardcodée par personnage.
            CharacterModelScaleNormalizer.NormalizeToUnitHeight(instance, _unitScale);

            var colliders = instance.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            _customModelRenderers.Clear();
            _customModelRenderers.AddRange(instance.GetComponentsInChildren<Renderer>());
            _bodyRenderer = instance.GetComponentInChildren<MeshRenderer>();

            SetupAnimator(instance, prefab);

            Material baseMat = CreateSafeUnitMaterial("UnitFactionRing_Mat");
            var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disk.name = "FactionRing";
            disk.transform.SetParent(_modelRoot, false);
            disk.transform.localPosition = new Vector3(0, 0.02f, 0);

            float ringFactor = _unit != null && _unit.FootprintType == Killtime.Tactics.Grid.TitanFootprintType.Rosette7 ? 2.85f : 0.95f;
            disk.transform.localScale = new Vector3(ringFactor, 0.02f, ringFactor) * _unitScale;

            var diskCol = disk.GetComponent<Collider>();
            if (diskCol != null) Destroy(diskCol);

            _teamDiskRenderer = disk.GetComponent<MeshRenderer>();
            _teamDiskRenderer.sharedMaterial = baseMat;

            _isCustomModel = true;
            _currentBodyColor = _unit != null && _unit.IsPlayerControlled 
                ? new Color(0.15f, 0.45f, 0.85f) 
                : new Color(0.85f, 0.25f, 0.2f);

            ApplyBodyColor(_currentBodyColor);
            return true;
        }

        public void BuildProceduralAvatar()
        {
            _isCustomModel = false;
            _hasWalkingClip = false;
            _hasWalkMultiplierParam = false;
            _customModelRenderers.Clear();
            _teamDiskRenderer = null;
            _animator = null;
            _animatorOverride = null;

            if (_modelRoot != null) Destroy(_modelRoot.gameObject);

            _modelRoot = new GameObject("AvatarModel").transform;
            _modelRoot.SetParent(transform, false);

            Material baseMat = CreateSafeUnitMaterial("UnitAvatar_Mat");

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "BodyCapsule";
            body.transform.SetParent(_modelRoot, false);
            body.transform.localPosition = new Vector3(0, 1.0f * _unitScale, 0);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f) * _unitScale;

            var capsuleCol = body.GetComponent<Collider>();
            if (capsuleCol != null) Destroy(capsuleCol);

            _bodyRenderer = body.GetComponent<MeshRenderer>();
            _bodyRenderer.sharedMaterial = baseMat;
            _currentBodyColor = _unit != null && _unit.IsPlayerControlled ? new Color(0.15f, 0.45f, 0.85f) : new Color(0.85f, 0.25f, 0.2f);
            ApplyBodyColor(_currentBodyColor);

            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            visor.transform.SetParent(_modelRoot, false);
            visor.transform.localPosition = new Vector3(0, 1.45f * _unitScale, 0.28f * _unitScale);
            visor.transform.localScale = new Vector3(0.45f, 0.15f, 0.25f) * _unitScale;

            var visorCol = visor.GetComponent<Collider>();
            if (visorCol != null) Destroy(visorCol);

            var visorRend = visor.GetComponent<MeshRenderer>();
            visorRend.sharedMaterial = baseMat;
            var visorProp = new MaterialPropertyBlock();
            Color vColor = _unit != null && _unit.IsPlayerControlled ? new Color(0.0f, 0.95f, 1.0f) : new Color(1.0f, 0.75f, 0.1f);
            visorProp.SetColor("_BaseColor", vColor);
            visorProp.SetColor("_Color", vColor);
            visorProp.SetColor("_EmissionColor", vColor * 1.5f);
            visorRend.SetPropertyBlock(visorProp);

            var pointer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pointer.name = "AimPointer";
            pointer.transform.SetParent(_modelRoot, false);
            pointer.transform.localPosition = new Vector3(0, 0.05f, 0.55f * _unitScale);
            pointer.transform.localRotation = Quaternion.Euler(90, 0, 0);
            pointer.transform.localScale = new Vector3(0.12f, 0.15f, 0.12f) * _unitScale;

            var pointerCol = pointer.GetComponent<Collider>();
            if (pointerCol != null) Destroy(pointerCol);

            var pointerRend = pointer.GetComponent<MeshRenderer>();
            pointerRend.sharedMaterial = baseMat;
            var pointerProp = new MaterialPropertyBlock();
            pointerProp.SetColor("_BaseColor", Color.white);
            pointerProp.SetColor("_Color", Color.white);
            pointerRend.SetPropertyBlock(pointerProp);
        }

        private void ApplyBodyColor(Color color)
        {
            if (_teamDiskRenderer != null)
            {
                _propBlock.SetColor("_BaseColor", color);
                _propBlock.SetColor("_Color", color);
                _teamDiskRenderer.SetPropertyBlock(_propBlock);
            }

            if (!_isCustomModel && _bodyRenderer != null)
            {
                _propBlock.SetColor("_BaseColor", color);
                _propBlock.SetColor("_Color", color);
                _bodyRenderer.SetPropertyBlock(_propBlock);
            }
        }

        public void TriggerHitFlash()
        {
            _hitFlashTimer = 0.25f;

            if (_isCustomModel)
            {
                _propBlock.SetColor("_BaseColor", Color.white);
                _propBlock.SetColor("_Color", Color.white);
                for (int i = 0; i < _customModelRenderers.Count; i++)
                {
                    if (_customModelRenderers[i] != null)
                    {
                        _customModelRenderers[i].SetPropertyBlock(_propBlock);
                    }
                }
            }
            else
            {
                ApplyBodyColor(Color.white);
            }
        }

        private void UpdateHitFlash()
        {
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= Time.deltaTime;
                if (_hitFlashTimer <= 0f)
                {
                    if (_isCustomModel)
                    {
                        for (int i = 0; i < _customModelRenderers.Count; i++)
                        {
                            if (_customModelRenderers[i] != null)
                            {
                                _customModelRenderers[i].SetPropertyBlock(null);
                            }
                        }
                    }
                    ApplyBodyColor(_currentBodyColor);
                }
            }
        }

        private void UpdateKOAnimation()
        {
            if (_unit == null || _unit.Stats == null) return;
            bool isDown = !_unit.Stats.IsAlive || _unit.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient);

            if (isDown)
            {
                // Filet de sécurité : tout passage à l'inconscience / mort lâche
                // l'arme tenue (même si TriggerFallingBackDeath n'a pas été appelé,
                // ex. flag posé direct via Stats / CombatDevArena / VTT).
                if (!_heldItemsDroppedOnKO)
                {
                    try { DropHeldItemsWithPhysics(); } catch { }
                }
                if (!_isUnconscious)
                {
                    _isUnconscious = true;
                    TriggerFallingBackDeath();
                }

                if (!_isCustomModel)
                {
                    Quaternion targetRot = Quaternion.Euler(-80f, 0f, 0f);
                    if (_modelRoot != null)
                    {
                        _modelRoot.localRotation = Quaternion.Slerp(_modelRoot.localRotation, targetRot, Time.deltaTime * 6f);
                        _modelRoot.localPosition = Vector3.Lerp(_modelRoot.localPosition, new Vector3(0f, 0.1f, 0f), Time.deltaTime * 6f);
                    }
                }
                else if (_modelRoot != null)
                {
                    _modelRoot.localRotation = Quaternion.Slerp(_modelRoot.localRotation, Quaternion.identity, Time.deltaTime * 6f);
                    _modelRoot.localPosition = Vector3.Lerp(_modelRoot.localPosition, Vector3.zero, Time.deltaTime * 6f);
                }
            }
            else
            {
                if (_isUnconscious)
                {
                    _isUnconscious = false;
                    // Réarmé : un prochain K.O. lâchera la (nouvelle) arme tenue.
                    _heldItemsDroppedOnKO = false;
                    if (_animator != null)
                    {
                        _animator.SetBool(AnimIsKO, false);
                        _animator.SetInteger(AnimWeaponType, GetCurrentWeaponType());
                        _animator.CrossFadeInFixedTime(_isInCombat ? GetCurrentCombatIdleStateName() : "Standing Idle", 0.25f);
                    }
                }

                if (_modelRoot != null)
                {
                    _modelRoot.localRotation = Quaternion.Slerp(_modelRoot.localRotation, Quaternion.identity, Time.deltaTime * 6f);
                    _modelRoot.localPosition = Vector3.Lerp(_modelRoot.localPosition, Vector3.zero, Time.deltaTime * 6f);
                }
            }
        }

        public void SpawnFloatingText(string message, Color color)
        {
            // Brouillard de guerre : aucun indice visuel ne doit fuir la position
            // d'un ennemi non détecté (ni dégâts flottants, ni logs 3D).
            if (_hiddenByFog) return;
            float stackOffset = _floatingTexts.Count * 0.38f;
            Vector3 headPos = GetHeadWorldPosition();
            bool hasStatus = _unit != null && _unit.Stats != null && _unit.Stats.ActiveStatus != StatusEffect.None;
            float baseHeight = hasStatus ? 0.95f : 0.65f;

            _floatingTexts.Add(new FloatingText
            {
                Text = message,
                Color = color,
                WorldPos = headPos + Vector3.up * (baseHeight + stackOffset),
                Lifetime = 3.6f,
                MaxLifetime = 3.6f
            });
        }

        private void UpdateFloatingTexts()
        {
            for (int i = _floatingTexts.Count - 1; i >= 0; i--)
            {
                var ft = _floatingTexts[i];
                ft.Lifetime -= Time.unscaledDeltaTime;
                ft.WorldPos += Vector3.up * (0.35f * Time.unscaledDeltaTime);

                if (ft.Lifetime <= 0f)
                {
                    _floatingTexts.RemoveAt(i);
                }
            }
        }

        private Vector3 GetHeadWorldPosition()
        {
            bool isDown = _unit.Stats != null && (!_unit.Stats.IsAlive || _unit.Stats.IsDead || _unit.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient));
            if (isDown)
            {
                return transform.position + Vector3.up * 0.75f;
            }

            if (_modelRoot != null)
            {
                return _modelRoot.TransformPoint(new Vector3(0, 1.85f * _unitScale, 0));
            }
            return transform.position + Vector3.up * (1.85f * _unitScale);
        }

        public static bool ShowAnimationTelemetry = false;

        private void OnGUI()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null || _unit == null || _unit.Stats == null) return;

            Vector3 headBase = GetHeadWorldPosition();
            bool hasStatus = _unit.Stats.ActiveStatus != StatusEffect.None;

            Vector3 hudBoxPos = headBase + Vector3.up * (hasStatus ? 0.85f : 0.35f);
            Vector3 screenPos = cam.WorldToScreenPoint(hudBoxPos);

            if (screenPos.z > 0.5f)
            {
                float uiX = screenPos.x;
                float uiY = Screen.height - screenPos.y;

                // Liste des unités mise en cache pour un seul appel : sert à tous les tests de profondeur.
                var allUnits = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
                float hudAlpha = ComputeVisionAlpha(cam, hudBoxPos, allUnits);

                DrawOverheadHUD(uiX, uiY, hudAlpha);
                if (ShowAnimationTelemetry)
                {
                    DrawAnimationTelemetry(uiX, uiY);
                }
                DrawCircularStatusHalo(cam, allUnits);
                DrawFloatingCombatTexts(cam, allUnits);

                if (_hoveredStatusInfo.HasValue)
                {
                    DrawStatusTooltip(_hoveredStatusInfo.Value, Event.current.mousePosition);
                    _hoveredStatusInfo = null;
                }
            }
        }

        /// <summary>
        /// Alpha de vision : 1 si lisible, <see cref="_occludedIconAlpha"/> si cet avatar est
        /// plus loin qu'un autre dans la vision (masqué derrière un avatar ou un décor).
        /// Combine occlusion décor (raycast) + test écran/profondeur inter-avatars
        /// (les avatars procéduraux n'ont pas de colliders, d'où le test manuel).
        /// </summary>
        private float ComputeVisionAlpha(UnityEngine.Camera cam, Vector3 worldPos, TacticalUnit[] allUnits)
        {
            if (cam == null) return 1f;
            float myDist = Vector3.Distance(cam.transform.position, worldPos);
            if (myDist < 0.001f) return 1f;

            Vector3 toIcon = worldPos - cam.transform.position;
            float dist = toIcon.magnitude;
            if (dist < 0.001f) return 1f;
            Vector3 rayDir = toIcon / dist;

            // 1) Décor / obstacle devant l'icône (covers, props, murs).
            if (dist > 0.5f)
            {
                var ray = new Ray(cam.transform.position, toIcon / dist);
                if (Physics.Raycast(ray, out RaycastHit hit, dist - 0.4f))
                {
                    var hitUnit = hit.transform != null ? hit.transform.GetComponentInParent<TacticalUnit>() : null;
                    if (hitUnit == null || hitUnit != _unit)
                    {
                        return _occludedIconAlpha;
                    }
                }
            }

            // 2) Un autre avatar plus proche dans la même zone d'écran masque celui-ci.
            Vector3 myScreen = cam.WorldToScreenPoint(worldPos);
            if (myScreen.z < 0.1f) return 1f;

            if (allUnits == null || allUnits.Length == 0) return 1f;

            for (int i = 0; i < allUnits.Length; i++)
            {
                var other = allUnits[i];
                if (other == null || other == _unit || other.Stats == null) continue;

                Vector3 otherHead = other.transform.position + Vector3.up * 1.85f;
                float otherDist = Vector3.Distance(cam.transform.position, otherHead);
                if (otherDist + _occlusionDepthBias >= myDist) continue;

                // 2a) Test volumétrique 3D : le rayon caméra -> icône traverse-t-il le corps
                // de l'avatar plus proche ? Indispensable quand la caméra est collée à
                // l'avatar : sa tête est alors hors écran (ou derrière le near plane) donc
                // le test écran ci-dessous ne peut plus le détecter, alors que son corps
                // remplit l'écran et bouche la vue (cas du screenshot).
                Vector3 otherFeet = other.transform.position + Vector3.up * 0.15f;
                Vector3 otherCenter = other.transform.position + Vector3.up * 1.0f;
                float tBody = Vector3.Dot(otherCenter - cam.transform.position, rayDir);
                if (tBody > 0.05f && tBody + _occlusionDepthBias < myDist)
                {
                    Vector3 closestOnRay = cam.transform.position + rayDir * tBody;
                    float qy = Mathf.Clamp(closestOnRay.y, otherFeet.y, otherHead.y);
                    Vector3 closestOnBody = new Vector3(other.transform.position.x, qy, other.transform.position.z);
                    if (Vector3.Distance(closestOnRay, closestOnBody) < _occlusionBodyRadius)
                    {
                        return _occludedIconAlpha;
                    }
                }

                Vector3 otherScreen = cam.WorldToScreenPoint(otherHead);
                if (otherScreen.z < 0.1f) continue;

                float pixelDist = Vector2.Distance(
                    new Vector2(myScreen.x, myScreen.y),
                    new Vector2(otherScreen.x, otherScreen.y));

                // La borne haute est volontairement large : de près, l'avatar occupe une
                // grande partie de l'écran, le rayon de masquage doit grandir en conséquence.
                float adaptiveRadius = _occlusionScreenRadiusPx * Mathf.Clamp(10f / Mathf.Max(1f, otherDist), 0.6f, 6f);
                if (pixelDist < adaptiveRadius)
                {
                    return _occludedIconAlpha;
                }
            }

            return 1f;
        }

        private void DrawAnimationTelemetry(float x, float y)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            bool isSelected = TacticalSelectionManager.Instance != null && TacticalSelectionManager.Instance.PrimarySelected == _unit;
            bool isActive = _unit.IsPlayerControlled && _unit.Stats.IsAlive;
            if (!isSelected && !isActive) return;

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            var clipInfo = _animator.GetCurrentAnimatorClipInfo(0);
            AnimationClip curClip = (clipInfo != null && clipInfo.Length > 0) ? clipInfo[0].clip : null;
            string clipName = curClip != null ? curClip.name : "Sans Clip";
            bool isLooping = curClip != null && curClip.isLooping;

            float normTime = stateInfo.normalizedTime;
            float cyclePct = (normTime % 1f) * 100f;
            if (cyclePct < 0f) cyclePct += 100f;
            int loopCount = Mathf.Max(0, (int)normTime);

            bool inTrans = _animator.IsInTransition(0);

            float w = 220f;
            float h = inTrans ? 58f : 46f;
            float topY = y - 36f - h - 6f;
            Rect rect = new Rect(x - w * 0.5f, topY, w, h);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.015f, 0.035f, 0.065f, 0.94f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color accentCol = inTrans ? new Color(1.0f, 0.72f, 0.15f, 0.95f) : new Color(0.0f, 0.90f, 1.0f, 0.85f);
            Color prevCol = GUI.color;
            GUI.color = accentCol;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), Texture2D.whiteTexture);
            GUI.color = prevCol;

            string loopTag = isLooping 
                ? "<color=#00FF88>[Loop: OUI]</color>" 
                : "<color=#FF4444>[Loop: NON]</color>";

            string line1 = $"🎬 <b>{clipName}</b>  {loopTag}";
            string line2 = $"⏱️ Cycle: <b>{cyclePct:0}%</b> (Tour #{loopCount})";

            if (inTrans)
            {
                var transInfo = _animator.GetAnimatorTransitionInfo(0);
                var nextClips = _animator.GetNextAnimatorClipInfo(0);
                string nextName = (nextClips != null && nextClips.Length > 0 && nextClips[0].clip != null) ? nextClips[0].clip.name : "Suivant";
                line2 += $" | <color=#FFB830>➔ {nextName} ({transInfo.normalizedTime * 100f:0}%)</color>";
            }

            bool inCombat = _animator.GetBool(AnimIsInCombat);
            bool isMoving = _animator.GetBool(AnimIsMoving);
            string combatTag = inCombat ? "<color=#00FF88>OUI</color>" : "<color=#FF6666>NON</color>";
            string moveTag = isMoving ? "<color=#00FF88>OUI</color>" : "<color=#8899AA>NON</color>";
            string line3 = $"⚙️ Combat: {combatTag} | Marche: {moveTag}";

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                richText = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 4, 1, 1)
            };
            labelStyle.normal.textColor = new Color(0.90f, 0.95f, 1.0f, 0.95f);

            GUI.Label(new Rect(rect.x + 4, rect.y + 3, rect.width - 8, 14), line1, labelStyle);
            GUI.Label(new Rect(rect.x + 4, rect.y + 17, rect.width - 8, 14), line2, labelStyle);
            GUI.Label(new Rect(rect.x + 4, rect.y + 31, rect.width - 8, 14), line3, labelStyle);
        }

        private void DrawOverheadHUD(float x, float y, float visionAlpha = 1f)
        {
            var stats = _unit.Stats;
            float width = 170f;
            float height = 34f;
            Rect rect = new Rect(x - width * 0.5f, y - height, width, height);

            float a = Mathf.Clamp01(visionAlpha);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.015f, 0.025f, 0.045f, 0.88f * a);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color nameCol = _unit.IsPlayerControlled ? new Color(0.35f, 0.80f, 1f) : new Color(1f, 0.35f, 0.35f);
            nameCol.a *= a;
            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            nameStyle.normal.textColor = nameCol;
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, 15), stats.Name, nameStyle);

            float hpPct = Mathf.Clamp01((float)stats.CurrentHealth / Mathf.Max(1, stats.MaxHealth));
            Color hpColor = hpPct > 0.5f ? new Color(0.2f, 0.9f, 0.4f) : (hpPct > 0.25f ? Color.yellow : new Color(1f, 0.3f, 0.3f));
            hpColor.a *= a;
            var statStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            statStyle.normal.textColor = hpColor;
            GUI.Label(new Rect(rect.x + 4, rect.y + 16, rect.width - 8, 15), $"PV: {stats.CurrentHealth}/{stats.MaxHealth} | PA: {stats.CurrentActionPoints}/{stats.MaxActionPoints}", statStyle);
        }

        private void DrawCircularStatusHalo(UnityEngine.Camera cam, TacticalUnit[] allUnits = null)
        {
            if (_unit == null || _unit.Stats == null) return;

            var currentStatus = _unit.Stats.ActiveStatus;
            if (currentStatus == StatusEffect.None) return;

            var activeList = new List<StatusEffect>();
            for (int i = 0; i < _allStatusEffects.Length; i++)
            {
                var flag = _allStatusEffects[i];
                if (flag != StatusEffect.None && currentStatus.HasFlag(flag))
                {
                    activeList.Add(flag);
                }
            }

            int count = activeList.Count;
            if (count == 0) return;

            Vector3 headBase = GetHeadWorldPosition();
            Vector3 crownCenter = headBase + Vector3.up * 0.28f;

            float radius = count == 1 ? 0f : Mathf.Clamp(0.28f + (count * 0.04f), 0.30f, 0.52f) * _unitScale;
            float orbitSpeed = 0.80f;
            float baseAngle = Time.time * orbitSpeed;

            for (int i = 0; i < count; i++)
            {
                var status = activeList[i];
                var info = GetStatusVisualInfo(status);
                var iconTex = GetStatusIconTexture(status, info.PrimaryColor);

                Vector3 worldPos;
                if (count == 1)
                {
                    float bob = Mathf.Sin(Time.time * 2.8f) * 0.04f;
                    worldPos = crownCenter + Vector3.up * (0.08f + bob);
                }
                else
                {
                    float angle = baseAngle + (i * (Mathf.PI * 2f / count));
                    float bob = Mathf.Sin(Time.time * 2.4f + i * 1.2f) * 0.035f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, bob, Mathf.Sin(angle) * radius);
                    worldPos = crownCenter + offset;
                }

                Vector3 screenPoint = cam.WorldToScreenPoint(worldPos);
                if (screenPoint.z <= 0.3f) continue;

                float dist = Vector3.Distance(cam.transform.position, worldPos);
                float badgeSize = Mathf.Clamp(340f / Mathf.Max(1f, dist), 24f, 32f);

                Vector2 pos2D = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
                Rect badgeRect = new Rect(pos2D.x - badgeSize * 0.5f, pos2D.y - badgeSize * 0.5f, badgeSize, badgeSize);

                // Icône plus loin qu'un autre avatar dans la vision -> transparente.
                float iconAlpha = ComputeVisionAlpha(cam, worldPos, allUnits);
                Color prevIconCol = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(iconAlpha));
                GUI.DrawTexture(badgeRect, iconTex);
                GUI.color = prevIconCol;

                if (iconAlpha > 0.6f && badgeRect.Contains(Event.current.mousePosition))
                {
                    _hoveredStatusInfo = info;
                }
            }
        }

        private void DrawStatusTooltip(StatusVisualInfo info, Vector2 mousePos)
        {
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            titleStyle.normal.textColor = info.PrimaryColor;

            var descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                wordWrap = true
            };
            descStyle.normal.textColor = new Color(0.82f, 0.90f, 0.98f, 0.95f);

            float width = 250f;
            float textW = width - 20f;
            float descHeight = Mathf.Max(20f, descStyle.CalcHeight(new GUIContent(info.Description), textW));
            float height = 24f + descHeight + 10f;

            float x = Mathf.Clamp(mousePos.x + 14f, 10f, Screen.width - width - 10f);
            float y = Mathf.Clamp(mousePos.y - height - 6f, 10f, Screen.height - height - 10f);
            Rect rect = new Rect(x, y, width, height);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.012f, 0.022f, 0.040f, 0.96f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color prevCol = GUI.color;
            GUI.color = info.PrimaryColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.color = prevCol;

            GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, textW, 18f), $"[{info.Tag}] {info.Name}", titleStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 22f, textW, descHeight), info.Description, descStyle);
        }

        private static Texture2D GetStatusIconTexture(StatusEffect status, Color primary)
        {
            if (_statusIconCache.TryGetValue(status, out var cached) && cached != null)
            {
                return cached;
            }

            var loaded = Resources.Load<Texture2D>($"Icons/Status/{status}")
                      ?? Resources.Load<Texture2D>($"Status/{status}");

            if (loaded != null)
            {
                _statusIconCache[status] = loaded;
                return loaded;
            }

            var generated = GenerateProceduralStatusIcon(status, primary);
            _statusIconCache[status] = generated;
            return generated;
        }

        private static Texture2D GenerateProceduralStatusIcon(StatusEffect status, Color primary)
        {
            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - 31.5f) / 30f;
                    float v = (y - 31.5f) / 30f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    if (r > 0.98f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    if (r >= 0.80f)
                    {
                        float ringAlpha = Mathf.Clamp01((0.98f - r) / 0.06f) * Mathf.Clamp01((r - 0.80f) / 0.06f);
                        byte rB = (byte)(primary.r * 255);
                        byte gB = (byte)(primary.g * 255);
                        byte bB = (byte)(primary.b * 255);
                        byte aB = (byte)(ringAlpha * 255);
                        pixels[y * size + x] = new Color32(rB, gB, bB, aB);
                        continue;
                    }

                    // Fond en verre fumé semi-transparent : la scène 3D reste visible
                    // à travers le badge (seuls l'anneau et le glyphe restent opaques).
                    Color bg = new Color(0.02f, 0.04f, 0.07f, 0.35f);
                    if (r >= 0.72f)
                    {
                        float edge = (r - 0.72f) / 0.08f;
                        bg = Color.Lerp(bg, primary * 0.5f, edge);
                    }

                    float shape = EvaluateGlyphShape(status, u, v);
                    if (shape > 0f)
                    {
                        Color iconColor = Color.Lerp(primary, Color.white, 0.45f);
                        Color blended = Color.Lerp(bg, iconColor, Mathf.Clamp01(shape));
                        pixels[y * size + x] = (Color32)blended;
                    }
                    else
                    {
                        pixels[y * size + x] = (Color32)bg;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static float EvaluateGlyphShape(StatusEffect status, float u, float v)
        {
            switch (status)
            {
                case StatusEffect.Inconscient:
                    float dHead = Mathf.Sqrt(u * u + (v - 0.12f) * (v - 0.12f));
                    bool bone = (dHead <= 0.28f) || (Mathf.Abs(u) <= 0.16f && v >= -0.34f && v <= 0.05f);
                    float dEyeL = Mathf.Sqrt((u + 0.10f) * (u + 0.10f) + (v - 0.10f) * (v - 0.10f));
                    float dEyeR = Mathf.Sqrt((u - 0.10f) * (u - 0.10f) + (v - 0.10f) * (v - 0.10f));
                    bool eye = (dEyeL <= 0.075f) || (dEyeR <= 0.075f);
                    bool nose = (v >= -0.10f && v <= -0.02f && Mathf.Abs(u) <= (v + 0.10f) * 0.7f);
                    bool teeth = (v >= -0.34f && v <= -0.22f && (Mathf.Abs(u - 0.055f) <= 0.02f || Mathf.Abs(u + 0.055f) <= 0.02f));
                    return (bone && !eye && !nose && !teeth) ? 1f : 0f;

                case StatusEffect.Sonne:
                    float angleS = Mathf.Atan2(v, u);
                    float distS = Mathf.Sqrt(u * u + v * v);
                    float starR = 0.15f + 0.32f * Mathf.Pow(Mathf.Abs(Mathf.Cos(4f * angleS)), 3f);
                    return distS <= starR ? 1f : 0f;

                case StatusEffect.ATerre:
                    bool groundBar = Mathf.Abs(u) <= 0.44f && v >= -0.36f && v <= -0.22f;
                    bool fallenHead = Mathf.Sqrt((u + 0.30f) * (u + 0.30f) + (v + 0.12f) * (v + 0.12f)) <= 0.08f;
                    bool arrowShaft = Mathf.Abs(u) <= 0.06f && v >= 0.02f && v <= 0.40f;
                    bool arrowHead = v >= -0.14f && v <= 0.06f && Mathf.Abs(u) <= (0.06f - v) * 1.35f;
                    return (groundBar || fallenHead || arrowShaft || arrowHead) ? 1f : 0f;

                case StatusEffect.Destabilise:
                    float ru = u * 0.866f - v * 0.5f;
                    float rv = u * 0.5f + v * 0.866f;
                    bool diamond = (Mathf.Abs(ru) + Mathf.Abs(rv) <= 0.42f) && !(Mathf.Abs(ru) + Mathf.Abs(rv) <= 0.22f);
                    bool crack = Mathf.Abs(u) <= 0.045f && Mathf.Abs(v) <= 0.38f;
                    return (diamond || crack) ? 1f : 0f;

                case StatusEffect.Etourdi:
                    float d1 = Mathf.Abs(u) + Mathf.Abs(v - 0.24f);
                    float d2 = Mathf.Abs(u + 0.22f) + Mathf.Abs(v + 0.14f);
                    float d3 = Mathf.Abs(u - 0.22f) + Mathf.Abs(v + 0.14f);
                    return (d1 <= 0.15f || d2 <= 0.15f || d3 <= 0.15f) ? 1f : 0f;

                case StatusEffect.Immobilise:
                    bool lockBody = Mathf.Abs(u) <= 0.24f && v >= -0.36f && v <= 0.02f;
                    bool lockShackle = v >= 0.02f && v <= 0.38f && Mathf.Abs(u) <= 0.18f && !(v <= 0.30f && Mathf.Abs(u) <= 0.09f);
                    bool lockHole = (Mathf.Sqrt(u * u + (v + 0.12f) * (v + 0.12f)) <= 0.045f) || (Mathf.Abs(u) <= 0.025f && v >= -0.25f && v <= -0.12f);
                    return ((lockBody || lockShackle) && !lockHole) ? 1f : 0f;

                case StatusEffect.Paralyse:
                    bool seg1 = v >= 0.02f && v <= 0.46f && Mathf.Abs(u - (v - 0.02f) * 0.4f - 0.04f) <= 0.08f;
                    bool seg2 = v <= 0.04f && v >= -0.46f && Mathf.Abs(u - (v + 0.46f) * 0.4f + 0.12f) <= 0.08f;
                    bool midBar = Mathf.Abs(v - 0.03f) <= 0.05f && u >= -0.18f && u <= 0.18f;
                    return (seg1 || seg2 || midBar) ? 1f : 0f;

                case StatusEffect.Ralenti:
                    bool topTri = v >= 0f && v <= 0.36f && Mathf.Abs(u) <= (v * 0.85f + 0.03f);
                    bool botTri = v <= 0f && v >= -0.36f && Mathf.Abs(u) <= (-v * 0.85f + 0.03f);
                    bool hCaps = (v >= 0.36f && v <= 0.43f && Mathf.Abs(u) <= 0.32f) || (v <= -0.36f && v >= -0.43f && Mathf.Abs(u) <= 0.32f);
                    return (topTri || botTri || hCaps) ? 1f : 0f;

                case StatusEffect.Agonisant:
                    float hVal = (u * u + (v - 0.10f) * (v - 0.10f) - 0.20f);
                    bool inHeart = (hVal * hVal * hVal - u * u * (v - 0.10f) * (v - 0.10f) * (v - 0.10f)) <= 0f;
                    bool ecgSlash = Mathf.Abs(u - v * 0.4f) <= 0.04f;
                    return (inHeart && !ecgSlash) ? 1f : 0f;

                case StatusEffect.Aveugle:
                    bool inEye = v <= 0.30f * (1f - (u / 0.44f) * (u / 0.44f)) && v >= -0.30f * (1f - (u / 0.44f) * (u / 0.44f)) && Mathf.Abs(u) <= 0.44f;
                    bool slashEye = Mathf.Abs(u - v) <= 0.05f && Mathf.Abs(u) <= 0.46f;
                    return (inEye || slashEye) ? 1f : 0f;

                case StatusEffect.Sourd:
                    float dEar = Mathf.Sqrt((u - 0.04f) * (u - 0.04f) + v * v);
                    bool earShape = (dEar <= 0.32f && !(dEar <= 0.16f && u >= 0.04f));
                    bool slashEar = Mathf.Abs(u - v) <= 0.05f && Mathf.Abs(u) <= 0.46f;
                    return (earShape || slashEar) ? 1f : 0f;

                case StatusEffect.Asphyxie:
                    bool l1 = Mathf.Sqrt((u + 0.15f) * (u + 0.15f) + (v + 0.06f) * (v + 0.06f)) <= 0.18f;
                    bool l2 = Mathf.Sqrt((u - 0.15f) * (u - 0.15f) + (v + 0.06f) * (v + 0.06f)) <= 0.18f;
                    bool trac = Mathf.Abs(u) <= 0.05f && v >= -0.10f && v <= 0.40f;
                    bool band = Mathf.Abs(v - 0.18f) <= 0.04f && Mathf.Abs(u) <= 0.22f;
                    return ((l1 || l2 || trac) && !band) ? 1f : 0f;

                case StatusEffect.Empoisonne:
                    float dDrop = Mathf.Sqrt(u * u + (v + 0.10f) * (v + 0.10f));
                    bool baseDrop = dDrop <= 0.25f;
                    bool tipDrop = v >= -0.10f && v <= 0.44f && Mathf.Abs(u) <= (0.44f - v) * 0.46f;
                    bool crossHole = (Mathf.Abs(u) <= 0.035f && Mathf.Abs(v + 0.10f) <= 0.09f) || (Mathf.Abs(v + 0.10f) <= 0.035f && Mathf.Abs(u) <= 0.09f);
                    return ((baseDrop || tipDrop) && !crossHole) ? 1f : 0f;

                case StatusEffect.EnFeu:
                    float fBase = Mathf.Sqrt(u * u + (v + 0.14f) * (v + 0.14f));
                    bool b1 = fBase <= 0.28f && v <= 0.05f;
                    bool bMain = v >= -0.05f && v <= 0.48f && Mathf.Abs(u) <= (0.48f - v) * 0.42f;
                    bool bL = v >= -0.05f && v <= 0.25f && Mathf.Abs(u + 0.12f) <= (0.25f - v) * 0.45f;
                    bool bR = v >= -0.05f && v <= 0.30f && Mathf.Abs(u - 0.12f) <= (0.30f - v) * 0.45f;
                    return (b1 || bMain || bL || bR) ? 1f : 0f;

                case StatusEffect.Saignement:
                    float dDrop1 = Mathf.Sqrt((u + 0.06f) * (u + 0.06f) + (v - 0.04f) * (v - 0.04f));
                    bool mainD = (dDrop1 <= 0.20f) || (v >= 0.04f && v <= 0.42f && Mathf.Abs(u + 0.06f) <= (0.42f - v) * 0.45f);
                    float dDrop2 = Mathf.Sqrt((u - 0.20f) * (u - 0.20f) + (v + 0.24f) * (v + 0.24f));
                    bool smallD = (dDrop2 <= 0.10f) || (v >= -0.24f && v <= -0.08f && Mathf.Abs(u - 0.20f) <= (-0.08f - v) * 0.5f);
                    return (mainD || smallD) ? 1f : 0f;

                case StatusEffect.ChronoFracture:
                    float dDial = Mathf.Sqrt(u * u + v * v);
                    bool dial = dDial <= 0.36f && !(dDial <= 0.26f);
                    bool handH = Mathf.Abs(v) <= 0.035f && u >= 0f && u <= 0.24f;
                    bool handV = Mathf.Abs(u) <= 0.035f && v >= 0f && v <= 0.26f;
                    bool fracture = Mathf.Abs(u - v * 0.5f) <= 0.04f && u >= 0.10f;
                    return (dial || handH || handV || fracture) ? 1f : 0f;

                default:
                    return (Mathf.Abs(u) + Mathf.Abs(v) <= 0.35f) ? 1f : 0f;
            }
        }

        private static StatusVisualInfo GetStatusVisualInfo(StatusEffect status)
        {
            return status switch
            {
                StatusEffect.Destabilise => new StatusVisualInfo { Status = status, Tag = "DST", Name = "DÉSTABILISÉ", Description = "-2 aux épreuves, coût PA x2 pour marcher", PrimaryColor = new Color(0.15f, 0.85f, 1f) },
                StatusEffect.Etourdi => new StatusVisualInfo { Status = status, Tag = "STN", Name = "ÉTOURDI", Description = "-1 sur tous les jets", PrimaryColor = new Color(1f, 0.85f, 0.2f) },
                StatusEffect.Immobilise => new StatusVisualInfo { Status = status, Tag = "IMM", Name = "IMMOBILISÉ", Description = "Mouvement impossible, -2 esquive", PrimaryColor = new Color(1f, 0.45f, 0.15f) },
                StatusEffect.Paralyse => new StatusVisualInfo { Status = status, Tag = "PAR", Name = "PARALYSÉ", Description = "Incapacité totale d'action/réaction", PrimaryColor = new Color(0.1f, 0.95f, 1f) },
                StatusEffect.Sonne => new StatusVisualInfo { Status = status, Tag = "SON", Name = "SONNÉ", Description = "Tombe à terre, aucune action possible", PrimaryColor = new Color(1f, 0.65f, 0.1f) },
                StatusEffect.ATerre => new StatusVisualInfo { Status = status, Tag = "PRN", Name = "À TERRE", Description = "-1 en attaque, -2 en défense/esquive", PrimaryColor = new Color(0.45f, 0.75f, 1f) },
                StatusEffect.Ralenti => new StatusVisualInfo { Status = status, Tag = "SLW", Name = "RALENTI", Description = "Toutes les actions coûtent le double de PA", PrimaryColor = new Color(0.3f, 0.9f, 0.95f) },
                StatusEffect.Agonisant => new StatusVisualInfo { Status = status, Tag = "AGN", Name = "AGONISANT", Description = "Panique motrice, détresse vitale (-2)", PrimaryColor = new Color(0.95f, 0.2f, 0.85f) },
                StatusEffect.Inconscient => new StatusVisualInfo { Status = status, Tag = "KO", Name = "INCONSCIENT", Description = "K.O. total / Syncope (Parade impossible)", PrimaryColor = new Color(0.35f, 0.65f, 1f) },
                StatusEffect.Aveugle => new StatusVisualInfo { Status = status, Tag = "AVG", Name = "AVEUGLE", Description = "-4 attaques à distance, -2 en mêlée", PrimaryColor = new Color(0.7f, 0.7f, 0.8f) },
                StatusEffect.Sourd => new StatusVisualInfo { Status = status, Tag = "SRD", Name = "SOURD", Description = "Perte d'initiative réflexe", PrimaryColor = new Color(0.75f, 0.75f, 0.8f) },
                StatusEffect.Asphyxie => new StatusVisualInfo { Status = status, Tag = "ASP", Name = "ASPHYXIE", Description = "Perte continue de PA, suffocation", PrimaryColor = new Color(0.2f, 0.95f, 0.75f) },
                StatusEffect.Empoisonne => new StatusVisualInfo { Status = status, Tag = "TOX", Name = "EMPOISONNÉ", Description = "Dégâts toxiques récurrents par tour", PrimaryColor = new Color(0.35f, 0.95f, 0.25f) },
                StatusEffect.EnFeu => new StatusVisualInfo { Status = status, Tag = "BRN", Name = "EN FEU", Description = "Dégâts thermiques continus", PrimaryColor = new Color(1f, 0.35f, 0.1f) },
                StatusEffect.Saignement => new StatusVisualInfo { Status = status, Tag = "BLD", Name = "SAIGNEMENT", Description = "Hémorragie active, perte continue de PV", PrimaryColor = new Color(1f, 0.2f, 0.25f) },
                StatusEffect.ChronoFracture => new StatusVisualInfo { Status = status, Tag = "CHR", Name = "CHRONO-FRACTURE", Description = "Déphasage causal du Fleuve du Temps", PrimaryColor = new Color(0f, 0.95f, 1f) },
                _ => new StatusVisualInfo { Status = status, Tag = "ALT", Name = "ALTÉRATION", Description = "Statut actif", PrimaryColor = Color.yellow }
            };
        }

        private void DrawFloatingCombatTexts(UnityEngine.Camera cam, TacticalUnit[] allUnits = null)
        {
            if (_hiddenByFog) return;
            for (int i = 0; i < _floatingTexts.Count; i++)
            {
                var ft = _floatingTexts[i];
                Vector3 sp = cam.WorldToScreenPoint(ft.WorldPos);
                if (sp.z > 0.3f)
                {
                    float lifetimeAlpha = Mathf.Clamp01(ft.Lifetime / (ft.MaxLifetime * 0.35f));
                    float visionAlpha = ComputeVisionAlpha(cam, ft.WorldPos, allUnits);
                    float alpha = lifetimeAlpha * Mathf.Clamp01(visionAlpha);
                    Color c = ft.Color;
                    c.a = alpha;

                    var style = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 13,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap = false
                    };

                    GUIContent content = new GUIContent(ft.Text);
                    Vector2 size = style.CalcSize(content);
                    float boxWidth = Mathf.Max(size.x + 24f, 150f);
                    float boxHeight = Mathf.Max(size.y + 6f, 24f);

                    float clampedX = Mathf.Clamp(sp.x, boxWidth * 0.5f + 10f, Screen.width - boxWidth * 0.5f - 10f);
                    float clampedY = Mathf.Clamp(Screen.height - sp.y, boxHeight * 0.5f + 10f, Screen.height - boxHeight * 0.5f - 10f);

                    Rect boxRect = new Rect(clampedX - boxWidth * 0.5f, clampedY - boxHeight * 0.5f, boxWidth, boxHeight);

                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.02f, 0.035f, 0.06f, 0.90f * alpha);
                    GUI.Box(boxRect, GUIContent.none);
                    GUI.backgroundColor = prevBg;

                    style.normal.textColor = new Color(0f, 0f, 0f, 0.95f * alpha);
                    GUI.Label(new Rect(boxRect.x + 1, boxRect.y + 1, boxRect.width, boxRect.height), ft.Text, style);

                    style.normal.textColor = c;
                    GUI.Label(boxRect, ft.Text, style);
                }
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------
        // Brouillard de guerre asymétrique : invisibilité des non-détectés.
        // ------------------------------------------------------------------

        private bool _hiddenByFog;

        /// <summary>Vrai quand cet avatar est masqué par le brouillard local.</summary>
        public bool IsHiddenByFog => _hiddenByFog;

        /// <summary>
        /// Masque / révèle l'avatar sans toucher à la logique (pathfinding, PV, PA).
        /// Masqué : modèle 3D + arme équipée désactivés, collider insensible au clic
        /// (impossible de cibler un ennemi invisible), textes flottants purgés.
        /// La logique (position, tour, IA, MJ) continue de tourner normalement.
        /// </summary>
        public void SetHiddenByFog(bool hidden)
        {
            if (_hiddenByFog == hidden) return;
            _hiddenByFog = hidden;
            try
            {
                if (_modelRoot != null) _modelRoot.gameObject.SetActive(!hidden);
                if (_equippedWeaponInstance != null) _equippedWeaponInstance.SetActive(!hidden);
                var col = GetComponent<Collider>();
                if (col != null) col.enabled = !hidden;
                if (hidden) _floatingTexts.Clear();
            }
            catch { /* ignore */ }
        }
    }
}

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
    public partial class TacticalUnitVisual : MonoBehaviour
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
            UpdateDuoTechHighlight();
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
    }
}

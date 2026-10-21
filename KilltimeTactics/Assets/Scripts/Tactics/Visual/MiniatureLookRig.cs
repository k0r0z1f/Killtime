using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Killtime.Tactics.Units;

namespace Killtime.Tactics.Visual
{
    /// <summary>
    /// RD-207 (amélioration) : vrai constraint Rigging — regard tête.
    /// MultiAimConstraint sur HumanBodyBones.Head vers une cible "LookTarget"
    /// par instance (décalée latéralement/hauteur, seedée) : deux clones du même
    /// asset ne regardent pas au même endroit. Poids 0.30 + jitter 0.15.
    /// Regard coupé à la perte de conscience (SetAlive), racine intacte.
    /// Sans tête humanoïde : désactivé gracieusement (pas de Rig créé).
    /// </summary>
    public sealed class MiniatureLookRig : MonoBehaviour
    {
        public const float BaseWeight = 0.3f;
        public const float JitterRange = 0.15f;
        public const float GazeHeight = 1.5f;

        private Rig _rig;
        private MultiAimConstraint _aim;
        private RigBuilder _builder;
        private GameObject _builderHost;
        private bool _built;
        private bool _alive = true;
        private TacticalUnit _owner;
        private Transform _lookTarget;
        private Vector3 _fallbackLocalPos;
        private float _retargetTimer;
        private float _retargetPeriod = 0.4f;

        /// <summary>Attache idempotent (un seul par unité, parenté incluse).</summary>
        public static MiniatureLookRig AttachTo(GameObject go)
        {
            if (go == null) return null;
            var existing = go.GetComponent<MiniatureLookRig>()
                ?? go.GetComponentInParent<MiniatureLookRig>()
                ?? go.GetComponentInChildren<MiniatureLookRig>();
            if (existing != null) return existing;
            var created = go.AddComponent<MiniatureLookRig>();
            created.EnsureBuilt();
            return created;
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        /// <summary>Construit une fois (Awake Play + explicite EditMode).</summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            Build();
        }

        private void OnDestroy()
        {
            if (_aim != null) Destroy(_aim);
        }

        /// <summary>Coupe (mort/K.O.) ou restaure le regard. No-op sans Rig.</summary>
        public void SetAlive(bool alive)
        {
            _alive = alive;
            if (_rig != null) _rig.weight = alive ? 1f : 0f;
        }

        // Introspection (tests EditMode, sans référence Rigging côté tests).
        public bool HasRigSetup => _rig != null && _aim != null;
        public bool HasLookTarget => _lookTarget != null;
        public float AimWeight => _aim != null ? _aim.weight : -1f;
        public float RigWeight => _rig != null ? _rig.weight : -1f;
        public int RigLayerCount => _builder != null ? _builder.layers.Count : 0;
        public int AimSourceCount => _aim != null ? _aim.data.sourceObjects.Count : 0;
        public bool BuilderOnAnimator => _builderHost != null
            && _builderHost.GetComponent<Animator>() != null;

        private void Update()
        {
            if (!_alive || _rig == null || _lookTarget == null || _owner == null) return;
            _retargetTimer -= Time.deltaTime;
            if (_retargetTimer > 0f) return;
            _retargetTimer = _retargetPeriod;
            _candidates.Clear();
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u == _owner) continue;
                var stats = u.Stats;
                _candidates.Add(new MiniatureSeeds.GazeCandidate(u.transform.position + Vector3.up * GazeHeight,
                    u.IsPlayerControlled, stats != null && stats.IsAlive, !u.IsStealthed));
            }
            var anchor = _lookTarget.parent;
            if (anchor == null) return;
            _lookTarget.position = MiniatureSeeds.PickGazePoint(transform.position, _owner.IsPlayerControlled,
                _candidates, anchor.TransformPoint(_fallbackLocalPos));
        }

        private readonly List<MiniatureSeeds.GazeCandidate> _candidates = new();

        private void Build()
        {
            var animator = GetComponentInChildren<Animator>();
            Transform head = TryGetHeadBone(animator);
            if (head == null || animator == null)
            {
                enabled = false;
                return;
            }

            int seed = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(gameObject);

            var lookChild = transform.Find("LookTarget");
            GameObject lookGo = lookChild != null ? lookChild.gameObject : new GameObject("LookTarget");
            if (lookChild == null) lookGo.transform.SetParent(transform, false);
            float lateral = (MiniatureSeeds.ComputeJitterForSeed(seed, 201) - 0.5f) * 3f;
            float height = (MiniatureSeeds.ComputeJitterForSeed(seed, 202) - 0.5f) * 0.6f;
            lookGo.transform.localPosition = new Vector3(lateral, 1.5f + height, 3f);
            _lookTarget = lookGo.transform;
            _fallbackLocalPos = lookGo.transform.localPosition;
            _owner = GetComponentInParent<TacticalUnit>();
            _retargetPeriod = 0.4f + MiniatureSeeds.ComputeJitterForSeed(seed, 204) * 0.3f;
            _retargetTimer = MiniatureSeeds.ComputeJitterForSeed(seed, 205) * _retargetPeriod;

            // Hôte = objet de l'Animator : RigBuilder exige GetComponent<Animator> local.
            GameObject host = animator.gameObject;
            var rigChild = host.transform.Find("MiniatureRig");
            GameObject rigGo = rigChild != null ? rigChild.gameObject : new GameObject("MiniatureRig");
            if (rigChild == null) rigGo.transform.SetParent(host.transform, false);
            _rig = rigGo.GetComponent<Rig>();
            if (_rig == null) _rig = rigGo.AddComponent<Rig>();
            _rig.weight = 1f;

            _aim = head.GetComponent<MultiAimConstraint>();
            if (_aim == null) _aim = head.gameObject.AddComponent<MultiAimConstraint>();
            _aim.weight = BaseWeight + MiniatureSeeds.ComputeJitterForSeed(seed, 203) * JitterRange;
            var data = _aim.data;
            data.constrainedObject = head;
            var sources = new WeightedTransformArray(0);
            sources.Add(new WeightedTransform(lookGo.transform, 1f));
            data.sourceObjects = sources;
            data.maintainOffset = true;
            data.aimAxis = MultiAimConstraintData.Axis.Z;
            _aim.data = data;

            var builder = host.GetComponent<RigBuilder>();
            if (builder == null) builder = host.AddComponent<RigBuilder>();
            builder.layers = new List<RigLayer> { new RigLayer(_rig) };
            _builder = builder;
            _builderHost = host;
        }

        /// <summary>Tête humanoïde mappée, ou null (jamais d'exception).</summary>
        private static Transform TryGetHeadBone(Animator animator)
        {
            try
            {
                if (animator == null) return null;
                var avatar = animator.avatar;
                if (avatar == null || !avatar.isHuman) return null;
                return animator.GetBoneTransform(HumanBodyBones.Head);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MiniatureLookRig] Tête humanoïde introuvable : {e.Message}");
                return null;
            }
        }
    }
}

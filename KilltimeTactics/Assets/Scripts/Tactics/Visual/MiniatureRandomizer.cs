using System;
using System.Reflection;
using UnityEngine;

namespace Killtime.Tactics.Visual
{
    /// <summary>
    /// Choix figé (RD-207) : figurines vivantes par défaut. Le mode plastique
    /// (slide/hop/tilt, sans jitter de timing) reste disponible pour les décors.
    /// </summary>
    public enum MiniatureStyle
    {
        Living,
        Plastic
    }

    /// <summary>
    /// RD-207 : fondation Animation Rigging + randomizer miniature.
    /// Au spawn : Animator.speed = 0.85-1.15 (seed = instance) + jitter du poids
    /// des contraintes Rigging par instance + micro-pose sur le modèle (racine
    /// de l'unité intacte : logique de grille non affectée).
    /// Fonctionne avec ou sans le package com.unity.animation.rigging (réflexion,
    /// no-op gracieux si absent) et ne touche pas au PlayerAnimator/BlendTree.
    /// </summary>
    public sealed class MiniatureRandomizer : MonoBehaviour
    {
        /// <summary>Choix figé : figurines vivantes (défaut).</summary>
        public static MiniatureStyle CurrentStyle = MiniatureStyle.Living;

        /// <summary>Attache idempotent (un seul par unité, parenté incluse).</summary>
        public static MiniatureRandomizer AttachTo(GameObject go)
        {
            if (go == null) return null;
            var existing = go.GetComponent<MiniatureRandomizer>()
                ?? go.GetComponentInParent<MiniatureRandomizer>()
                ?? go.GetComponentInChildren<MiniatureRandomizer>();
            if (existing != null) return existing;
            var created = go.AddComponent<MiniatureRandomizer>();
            created.EnsureApplied();
            return created;
        }

        private void Awake()
        {
            EnsureApplied();
        }

        /// <summary>Applique une fois (Awake Play + explicite EditMode).</summary>
        public void EnsureApplied()
        {
            if (_applied) return;
            _applied = true;
            Apply(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(gameObject));
        }

        private bool _applied;

        // Pose d'origine capturée (sérialisée : survit au rechargement) pour
        // ré-appliquer depuis la base au lieu d'empiler (yaw/échelle dériveraient).
        [SerializeField] private bool _poseCaptured;
        [SerializeField] private Vector3 _basePos;
        [SerializeField] private Quaternion _baseRot;
        [SerializeField] private Vector3 _baseScale;
        [SerializeField] private float _baseSpeed = 1f;

        /// <summary>Ré-applique depuis la pose capturée (idempotent, changement de style). Public.</summary>
        public void Apply(int seed)
        {
            MiniatureLookRig.AttachTo(gameObject);
            var animator = GetComponentInChildren<Animator>();
            bool firstCapture = false;
            if (animator != null)
            {
                var model = animator.transform;
                if (!_poseCaptured)
                {
                    _basePos = model.localPosition;
                    _baseRot = model.localRotation;
                    _baseScale = model.localScale;
                    _baseSpeed = animator.speed;
                    _poseCaptured = true;
                    firstCapture = true;
                }
                else
                {
                    model.localPosition = _basePos;
                    model.localRotation = _baseRot;
                    model.localScale = _baseScale;
                    animator.speed = _baseSpeed;
                }
                animator.speed = CurrentStyle == MiniatureStyle.Plastic
                    ? 1f
                    : MiniatureSeeds.ComputeSpeedForSeed(seed);
                ApplyModelMicroPose(model, seed);
            }
            EnsureRigSetup(animator != null ? animator.gameObject : gameObject);
            // Jitter des contraintes une seule fois : multiplicatif, il dériverait sinon.
            if (firstCapture) JitterConstraintWeights(seed);
        }

        private void ApplyModelMicroPose(Transform model, int seed)
        {
            if (model == null) return;
            model.localScale *= MiniatureSeeds.ComputeScaleForSeed(seed);
            if (CurrentStyle == MiniatureStyle.Plastic)
            {
                float tilt = (MiniatureSeeds.ComputeJitterForSeed(seed, 101) - 0.5f) * 8f;
                float hop = MiniatureSeeds.ComputeJitterForSeed(seed, 102) * 0.05f;
                model.localRotation *= Quaternion.Euler(0f, 0f, tilt);
                model.localPosition += new Vector3(0f, hop, 0f);
            }
            else
            {
                float yaw = (MiniatureSeeds.ComputeJitterForSeed(seed, 103) - 0.5f) * 8f;
                model.localRotation *= Quaternion.Euler(0f, yaw, 0f);
            }
        }

        /// <summary>RigBuilder + couche Rig vide sur l'hôte de l'Animator (réflexion).</summary>
        internal void EnsureRigSetup(GameObject host)
        {
            if (host == null) host = gameObject;
            try
            {
                var builderType = Type.GetType("UnityEngine.Animations.Rigging.RigBuilder, Unity.Animation.Rigging");
                var rigType = Type.GetType("UnityEngine.Animations.Rigging.Rig, Unity.Animation.Rigging");
                if (builderType == null || rigType == null) return;
                if (host.GetComponent(builderType) == null) host.AddComponent(builderType);
                var rigChild = host.transform.Find("MiniatureRig");
                if (rigChild == null)
                {
                    var rigGo = new GameObject("MiniatureRig");
                    rigGo.transform.SetParent(host.transform, false);
                    rigGo.AddComponent(rigType);
                    TryRegisterRigLayer(host, builderType, rigType, rigGo);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MiniatureRandomizer] Rig setup ignoré : {e.Message}");
            }
        }

        private void TryRegisterRigLayer(GameObject host, Type builderType, Type rigType, GameObject rigGo)
        {
            var builder = host.GetComponent(builderType);
            if (builder == null) return;
            var layersField = builderType.GetField("m_Layers",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (layersField == null) return;
            var layers = layersField.GetValue(builder) as System.Collections.IList;
            if (layers == null || layers.Count > 0) return;
            var genericArgs = layers.GetType().GetGenericArguments();
            if (genericArgs.Length == 0) return;
            var layerType = genericArgs[0];
            var layer = Activator.CreateInstance(layerType);
            var rigField = layerType.GetField("m_Rig",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            rigField?.SetValue(layer, rigGo.GetComponent(rigType));
            layers.Add(layer);
        }

        /// <summary>Jitter du poids des contraintes Rigging (namespace exact).</summary>
        internal void JitterConstraintWeights(int seed)
        {
            try
            {
                var constraints = GetComponentsInChildren<Component>();
                int index = 0;
                for (int i = 0; i < constraints.Length; i++)
                {
                    var c = constraints[i];
                    if (c == null) continue;
                    var type = c.GetType();
                    if (type.Namespace != "UnityEngine.Animations.Rigging") continue;
                    var weightProp = type.GetProperty("weight",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (weightProp == null || !weightProp.CanWrite) continue;
                    float jitter = 0.9f + MiniatureSeeds.ComputeJitterForSeed(seed, index++) * 0.1f;
                    float current = Convert.ToSingle(weightProp.GetValue(c, null));
                    weightProp.SetValue(c, Mathf.Clamp01(current * jitter), null);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MiniatureRandomizer] Jitter contraintes ignoré : {e.Message}");
            }
        }
    }
}

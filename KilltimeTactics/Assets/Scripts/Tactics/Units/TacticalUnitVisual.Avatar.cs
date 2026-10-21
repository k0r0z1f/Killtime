using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Killtime.Tactics.Units;
using Killtime.Tactics.CombatUI;
using Killtime.Tactics.Grid;
using Killtime.Core.Character;
using Killtime.Tactics.Visual;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Avatar : matériaux, custom model, procédural, hit-flash, DuoTech, KO.
    /// Façade : TacticalUnitVisual.cs (champs sérialisés, lifecycle). Aucune API changée.
    /// </summary>
    public partial class TacticalUnitVisual : MonoBehaviour
    {
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

            float ringFactor = 0.95f;
            if (_unit != null)
            {
                if (_unit.FootprintType == Killtime.Tactics.Grid.TitanFootprintType.Rosette7) ringFactor = 2.85f;
                else if (_unit.FootprintType == Killtime.Tactics.Grid.TitanFootprintType.Colossus19) ringFactor = 4.85f;
                else if (_unit.FootprintType == Killtime.Tactics.Grid.TitanFootprintType.Triangle3) ringFactor = 1.90f;
            }
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
                    if (!_isDuoTechTarget)
                    {
                        ApplyBodyColor(_currentBodyColor);
                    }
                }
            }
        }

        private bool _isDuoTechTarget = false;
        private Color _duoTechHighlightColor = new Color(1f, 0.45f, 0.1f);
        private float _duoTechPulseTime = 0f;

        /// <summary>
        /// Active ou désactive la surbrillance pulsante des cibles incluses dans la trajectoire Duo-Tech.
        /// </summary>
        public void SetDuoTechTargetHighlight(bool active, Color highlightColor)
        {
            if (_isDuoTechTarget == active && _duoTechHighlightColor == highlightColor) return;
            _isDuoTechTarget = active;
            _duoTechHighlightColor = highlightColor;

            if (!active)
            {
                if (_isCustomModel)
                {
                    for (int i = 0; i < _customModelRenderers.Count; i++)
                    {
                        if (_customModelRenderers[i] != null)
                            _customModelRenderers[i].SetPropertyBlock(null);
                    }
                }
                else if (_bodyRenderer != null)
                {
                    _propBlock.SetColor("_EmissionColor", Color.black);
                    _bodyRenderer.SetPropertyBlock(_propBlock);
                }
                ApplyBodyColor(_currentBodyColor);
            }
        }

        private void UpdateDuoTechHighlight()
        {
            if (!_isDuoTechTarget || _hitFlashTimer > 0f) return;
            _duoTechPulseTime += Time.deltaTime * 7f;
            float pulse = 0.65f + 0.35f * Mathf.Sin(_duoTechPulseTime);
            Color c = Color.Lerp(_duoTechHighlightColor, Color.white, pulse * 0.45f);

            if (_isCustomModel)
            {
                _propBlock.SetColor("_BaseColor", c);
                _propBlock.SetColor("_Color", c);
                _propBlock.SetColor("_EmissionColor", c * (1.1f * pulse));
                for (int i = 0; i < _customModelRenderers.Count; i++)
                {
                    if (_customModelRenderers[i] != null)
                        _customModelRenderers[i].SetPropertyBlock(_propBlock);
                }
            }
            else
            {
                ApplyBodyColor(c);
                if (_bodyRenderer != null)
                {
                    _propBlock.SetColor("_EmissionColor", c * (1.1f * pulse));
                    _bodyRenderer.SetPropertyBlock(_propBlock);
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
                    GetComponentInChildren<MiniatureLookRig>()?.SetAlive(true);
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
    }
}

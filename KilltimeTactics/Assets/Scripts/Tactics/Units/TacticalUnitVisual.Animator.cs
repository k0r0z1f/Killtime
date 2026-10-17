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
    /// Animator : paramètres, triggers, clips et variantes genrées.
    /// Façade : TacticalUnitVisual.cs (champs sérialisés, lifecycle). Aucune API changée.
    /// </summary>
    public partial class TacticalUnitVisual : MonoBehaviour
    {
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

            // BlendTree locomotion 1D (RD-025) : vitesse jeu en m/s, 0 à l'arrêt.
            // WalkSpeedMultiplier conservé pour la marche fusil (état simple).
            if (_hasSpeedParam)
            {
                float locomotionSpeed = moving && _unit != null ? Mathf.Max(0f, _unit.MoveSpeed) : 0f;
                _animator.SetFloat(AnimSpeed, locomotionSpeed);
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
            // Runs unisexes (RD-025) : les takes Mixamo s'appellent toutes "mixamo.com"
            // (Female comme Male), donc aucun override par nom n'est résolvable pour
            // "Medium Run"/"Fast Run" et les deux clips seraient ambigus. L'arbre utilise
            // les clips Female pour les deux sexes (retarget humanoid OK). Les variantes
            // Male seront activées après renommage des takes (suivi RD-024).
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
            _hasSpeedParam = false;
            _naturalWalkSpeed = 1.35f;

            if (_animator == null) return;

            for (int i = 0; i < _animator.parameterCount; i++)
            {
                int nameHash = _animator.parameters[i].nameHash;
                if (nameHash == AnimWalkSpeedMultiplier) _hasWalkMultiplierParam = true;
                else if (nameHash == AnimSpeed) _hasSpeedParam = true;
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
    }
}

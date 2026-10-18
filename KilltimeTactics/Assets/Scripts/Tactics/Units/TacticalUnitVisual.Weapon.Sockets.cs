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
    /// Armes (2/2) : sockets main/dos/holster, grips, rangement, drop physique.
    /// Façade : TacticalUnitVisual.cs (champs sérialisés, lifecycle). Aucune API changée.
    /// </summary>
    public partial class TacticalUnitVisual : MonoBehaviour
    {
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
    }
}
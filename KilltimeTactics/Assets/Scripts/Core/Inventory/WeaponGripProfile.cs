using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Core.Character;

namespace Killtime.Core.Inventory
{
    public enum WeaponGripSocket
    {
        RightHand = 0,
        LeftHand = 1,
        ChestTwoHands = 2,
        Back = 3,
        Holster = 4
    }

    [Serializable]
    public class WeaponGripProfile
    {
        public string Key = "";
        public WeaponGripSocket Socket = WeaponGripSocket.RightHand;
        public Vector3 PositionOffset = Vector3.zero;
        public Vector3 RotationOffset = Vector3.zero;
        public float TargetWorldLength = 0f;
        public float ScaleMultiplier = 1.0f;
        public Vector3 GripPivotOffset = Vector3.zero;

        public WeaponGripProfile Clone()
        {
            return new WeaponGripProfile
            {
                Key = this.Key,
                Socket = this.Socket,
                PositionOffset = this.PositionOffset,
                RotationOffset = this.RotationOffset,
                TargetWorldLength = this.TargetWorldLength,
                ScaleMultiplier = this.ScaleMultiplier,
                GripPivotOffset = this.GripPivotOffset
            };
        }
    }

    [Serializable]
    public class WeaponGripDatabase
    {
        public List<WeaponGripProfile> Profiles = new();
    }

    public static class WeaponGripService
    {
        private static WeaponGripDatabase _database;
        private static readonly Dictionary<string, WeaponGripProfile> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized = false;

        public static event Action OnProfilesChanged;

        private static string ConfigPath => Path.Combine(Application.persistentDataPath, "WeaponGripProfiles.json");

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            LoadFromDisk();
            _initialized = true;
        }

        public static IReadOnlyList<WeaponGripProfile> GetAllProfiles()
        {
            EnsureInitialized();
            return _database.Profiles;
        }

        public static WeaponGripProfile ResolveProfile(InventoryItem item)
        {
            EnsureInitialized();
            if (item == null) return CreateFallbackProfile("Default");

            if (!string.IsNullOrEmpty(item.Name) && _cache.TryGetValue(item.Name, out var pName))
                return pName;

            if (!string.IsNullOrEmpty(item.PrefabPath))
            {
                string pClean = Path.GetFileNameWithoutExtension(item.PrefabPath);
                if (_cache.TryGetValue(pClean, out var pPrefab))
                    return pPrefab;
            }

            if (!string.IsNullOrEmpty(item.PlaceholderKind) && _cache.TryGetValue(item.PlaceholderKind, out var pKind))
                return pKind;

            if (!string.IsNullOrEmpty(item.Category) && _cache.TryGetValue(item.Category, out var pCat))
                return pCat;

            return CreateFallbackProfile(item);
        }

        public static void SaveProfile(WeaponGripProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Key)) return;
            EnsureInitialized();

            int idx = _database.Profiles.FindIndex(p => string.Equals(p.Key, profile.Key, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _database.Profiles[idx] = profile.Clone();
            }
            else
            {
                _database.Profiles.Add(profile.Clone());
            }

            RebuildLookup();
            SaveToDisk();
            try { OnProfilesChanged?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        public static void ResetProfileToDefault(string key)
        {
            EnsureInitialized();
            _database.Profiles.RemoveAll(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            AddDefaultProfilesIfMissing();
            RebuildLookup();
            SaveToDisk();
            try { OnProfilesChanged?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        public static void ResetAllToCodexDefaults()
        {
            EnsureInitialized();
            _database.Profiles.Clear();
            AddDefaultProfilesIfMissing();
            RebuildLookup();
            SaveToDisk();
            try { OnProfilesChanged?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        public static void LoadFromDisk()
        {
            _database = null;
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    _database = JsonUtility.FromJson<WeaponGripDatabase>(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[WeaponGripService] Lecture impossible : {e.Message}");
                }
            }

            _database ??= new WeaponGripDatabase();
            AddDefaultProfilesIfMissing();
            RebuildLookup();
        }

        public static void SaveToDisk()
        {
            if (_database == null) return;
            try
            {
                string json = JsonUtility.ToJson(_database, true);
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WeaponGripService] Écriture échouée : {e.Message}");
            }
        }

        private static void RebuildLookup()
        {
            _cache.Clear();
            if (_database == null || _database.Profiles == null) return;
            for (int i = 0; i < _database.Profiles.Count; i++)
            {
                var p = _database.Profiles[i];
                if (p != null && !string.IsNullOrEmpty(p.Key))
                {
                    _cache[p.Key] = p;
                }
            }
        }

        private static void AddDefaultProfilesIfMissing()
        {
            _database ??= new WeaponGripDatabase();

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "Gourdin / Matraque",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.05f, 0.04f),
                RotationOffset = new Vector3(15f, 0f, 90f),
                TargetWorldLength = 0.65f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = new Vector3(0f, -0.15f, 0f)
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "Club",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.05f, 0.04f),
                RotationOffset = new Vector3(15f, 0f, 90f),
                TargetWorldLength = 0.65f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = new Vector3(0f, -0.15f, 0f)
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "SwordMetal",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.04f, 0f),
                RotationOffset = new Vector3(-75f, 180f, 0f),
                TargetWorldLength = 1.0f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "SwordMetalLong",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.04f, 0f),
                RotationOffset = new Vector3(-75f, 180f, 0f),
                TargetWorldLength = 1.35f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "LaserSword",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.04f, 0f),
                RotationOffset = new Vector3(-75f, 180f, 0f),
                TargetWorldLength = 1.0f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "LaserSwordLong",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.04f, 0f),
                RotationOffset = new Vector3(-75f, 180f, 0f),
                TargetWorldLength = 1.35f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "Spear",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.02f, 0f),
                RotationOffset = new Vector3(-80f, 180f, 0f),
                TargetWorldLength = 1.80f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = new Vector3(0f, -0.4f, 0f)
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "Axe",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.04f, 0f),
                RotationOffset = new Vector3(-75f, 180f, 0f),
                TargetWorldLength = 0.90f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = new Vector3(0f, -0.1f, 0f)
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "Hammer",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.04f, 0f),
                RotationOffset = new Vector3(-75f, 180f, 0f),
                TargetWorldLength = 1.05f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = new Vector3(0f, -0.15f, 0f)
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "RifleLaser",
                Socket = WeaponGripSocket.ChestTwoHands,
                PositionOffset = new Vector3(0.14f, 0.02f, 0.30f),
                RotationOffset = new Vector3(-6f, 0f, 0f),
                TargetWorldLength = 0.85f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "SniperLaser",
                Socket = WeaponGripSocket.ChestTwoHands,
                PositionOffset = new Vector3(0.14f, 0.02f, 0.32f),
                RotationOffset = new Vector3(-6f, 0f, 0f),
                TargetWorldLength = 1.10f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "Deglazer",
                Socket = WeaponGripSocket.ChestTwoHands,
                PositionOffset = new Vector3(0.14f, 0.02f, 0.30f),
                RotationOffset = new Vector3(-6f, 0f, 0f),
                TargetWorldLength = 1.00f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "GrenadeLauncher",
                Socket = WeaponGripSocket.ChestTwoHands,
                PositionOffset = new Vector3(0.14f, 0.02f, 0.30f),
                RotationOffset = new Vector3(-6f, 0f, 0f),
                TargetWorldLength = 0.85f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "PistolLaser",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = Vector3.zero,
                RotationOffset = new Vector3(-12f, 0f, 0f),
                TargetWorldLength = 0.32f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });

            EnsureDefaultProfile(new WeaponGripProfile
            {
                Key = "Grenade",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.02f, 0.04f, 0.02f),
                RotationOffset = new Vector3(0f, 0f, 0f),
                TargetWorldLength = 0.22f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            });
        }

        private static void EnsureDefaultProfile(WeaponGripProfile profile)
        {
            if (!_database.Profiles.Exists(p => string.Equals(p.Key, profile.Key, StringComparison.OrdinalIgnoreCase)))
            {
                _database.Profiles.Add(profile);
            }
        }

        private static WeaponGripProfile CreateFallbackProfile(InventoryItem item)
        {
            bool isRifle = IsRifle(item);
            var socket = isRifle ? WeaponGripSocket.ChestTwoHands : WeaponGripSocket.RightHand;
            float defaultLen = GetDefaultWeaponLength(item);

            return new WeaponGripProfile
            {
                Key = item != null ? item.Name : "Default",
                Socket = socket,
                PositionOffset = isRifle ? new Vector3(0.14f, 0.02f, 0.30f) : Vector3.zero,
                RotationOffset = isRifle ? new Vector3(-6f, 0f, 0f) : new Vector3(-75f, 180f, 0f),
                TargetWorldLength = defaultLen,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            };
        }

        private static WeaponGripProfile CreateFallbackProfile(string key)
        {
            return new WeaponGripProfile
            {
                Key = key,
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = Vector3.zero,
                RotationOffset = new Vector3(-75f, 180f, 0f),
                TargetWorldLength = 0.85f,
                ScaleMultiplier = 1.0f,
                GripPivotOffset = Vector3.zero
            };
        }

        public static bool IsRifle(InventoryItem item)
        {
            if (item == null || item.Type != ItemType.Weapon) return false;
            if (item.IsGrenade || item.IsThrowableGrenade()) return false;
            // Armes blanches (dont contondantes legacy) : jamais des fusils.
            if (SkillDefinitions.IsMeleeWeaponSkill(item.AssociatedSkill)) return false;
            if (item.EquipSlot == ItemEquipSlot.TwoHands) return true;
            if (item.AssociatedSkill == SkillType.Ballistique) return true;
            string n = ((item.Name ?? "") + " " + (item.PrefabPath ?? "")).ToLowerInvariant();
            return n.Contains("rifle") || n.Contains("gun") || n.Contains("fusil") || n.Contains("shotgun");
        }

        public static float GetDefaultWeaponLength(InventoryItem item)
        {
            if (item == null) return 0.85f;
            if (item.IsGrenade || item.IsThrowableGrenade()) return 0.22f;
            if (item.IsLauncher) return 0.85f;

            string kind = item.PlaceholderKind ?? "";
            string nm = (item.Name ?? "").ToLowerInvariant();

            if (nm.Contains("gourdin") || nm.Contains("matraque") || kind == "Club") return 0.65f;
            if (IsRifle(item))
            {
                if (kind == "SniperLaser") return 1.10f;
                if (kind == "Deglazer") return 1.00f;
                if (kind == "GrenadeLauncher") return 0.85f;
                if (kind == "Bow") return 1.25f;
                return 0.85f;
            }

            if (kind == "PistolLaser" || (item.AssociatedSkill == SkillType.Ballistique && item.RangeInTiles <= 6)) return 0.32f;
            if (kind == "SwordMetalLong" || kind == "LaserSwordLong") return 1.35f;
            if (kind == "Spear") return 1.80f;
            if (kind == "Hammer") return 1.05f;
            if (kind == "Axe") return 0.90f;
            if (kind == "SwordMetal" || kind == "LaserSword") return 1.00f;

            return 0.85f;
        }

        public static float MeasureBaseMeshLength(GameObject root, out int dominantAxis)
        {
            dominantAxis = 2;
            if (root == null) return 0.5f;

            bool hasBounds = false;
            Bounds totalBounds = default;

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                Matrix4x4 m = GetRelativeMatrix(mf.transform, root.transform);
                Bounds b = TransformLocalBounds(mf.sharedMesh.bounds, m);
                if (IsValidBounds(b))
                {
                    if (!hasBounds) { totalBounds = b; hasBounds = true; }
                    else totalBounds.Encapsulate(b);
                }
            }

            var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                var smr = skinned[i];
                if (smr == null || smr.sharedMesh == null) continue;
                Matrix4x4 m = GetRelativeMatrix(smr.transform, root.transform);
                Bounds b = TransformLocalBounds(smr.sharedMesh.bounds, m);
                if (IsValidBounds(b))
                {
                    if (!hasBounds) { totalBounds = b; hasBounds = true; }
                    else totalBounds.Encapsulate(b);
                }
            }

            if (!hasBounds) return 0.5f;

            Vector3 s = totalBounds.size;
            if (s.x >= s.y && s.x >= s.z) dominantAxis = 0;
            else if (s.y >= s.x && s.y >= s.z) dominantAxis = 1;
            else dominantAxis = 2;

            float maxDim = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            return maxDim > 0.01f ? maxDim : 0.5f;
        }

        private static Matrix4x4 GetRelativeMatrix(Transform child, Transform root)
        {
            Matrix4x4 m = Matrix4x4.TRS(child.localPosition, child.localRotation, child.localScale);
            Transform p = child.parent;
            while (p != null && p != root)
            {
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
                p = p.parent;
            }
            return m;
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

        private static bool IsValidBounds(Bounds b)
        {
            Vector3 s = b.size;
            return !(float.IsNaN(s.x) || float.IsNaN(s.y) || float.IsNaN(s.z)
                || float.IsInfinity(s.x) || float.IsInfinity(s.y) || float.IsInfinity(s.z))
                && (s.x + s.y + s.z) > 1e-6f;
        }

        public static Vector3 ComputeWeaponLocalPosition(Transform socket, Transform characterTransform, WeaponGripProfile profile, float unitScale)
        {
            float parentScale = 1f;
            if (socket != null)
            {
                Vector3 lossy = socket.lossyScale;
                float avg = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
                if (avg > 1e-4f) parentScale = avg;
            }

            Vector3 offset = profile.PositionOffset + profile.GripPivotOffset;
            if (profile.Socket == WeaponGripSocket.ChestTwoHands)
            {
                Vector3 worldOffset = offset.sqrMagnitude > 1e-8f ? offset : new Vector3(0.14f, 0.02f, 0.30f);
                if (socket == characterTransform)
                {
                    worldOffset.y += 1.30f * Mathf.Max(unitScale, 1e-3f);
                }
                return worldOffset / parentScale;
            }

            return offset / parentScale;
        }

        public static Quaternion ComputeWeaponLocalRotation(GameObject weaponInstance, Transform socket, Transform characterTransform, WeaponGripProfile profile, InventoryItem item)
        {
            bool isRifle = (profile.Socket == WeaponGripSocket.ChestTwoHands);
            bool isPistol = (profile.Socket == WeaponGripSocket.RightHand || profile.Socket == WeaponGripSocket.LeftHand)
                && (item != null && item.AssociatedSkill == SkillType.Ballistique && item.RangeInTiles > 1 && !isRifle);

            if (isRifle || isPistol)
            {
                int axis = 2;
                MeasureBaseMeshLength(weaponInstance, out axis);
                Quaternion align = (axis == 0) ? Quaternion.Euler(0f, -90f, 0f) : (axis == 1 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity);

                Quaternion charWorld = characterTransform != null ? characterTransform.rotation : Quaternion.identity;
                Quaternion tilt = Quaternion.Euler(profile.RotationOffset);
                Quaternion desiredWorld = charWorld * tilt * align;

                Quaternion boneWorld = socket != null ? socket.rotation : charWorld;
                if (boneWorld.x * boneWorld.x + boneWorld.y * boneWorld.y + boneWorld.z * boneWorld.z + boneWorld.w * boneWorld.w < 1e-6f)
                    return tilt * align;

                Quaternion local = Quaternion.Inverse(boneWorld) * desiredWorld;
                if (float.IsNaN(local.x + local.y + local.z + local.w) || float.IsInfinity(local.x + local.y + local.z + local.w))
                    return tilt * align;

                return local;
            }

            return Quaternion.Euler(profile.RotationOffset);
        }

        public static Vector3 ComputeWeaponLocalScale(GameObject weaponInstance, Transform socket, WeaponGripProfile profile, InventoryItem item, float unitScale)
        {
            float baseLen = MeasureBaseMeshLength(weaponInstance, out _);
            float targetWorldLen = (profile != null && profile.TargetWorldLength > 0.05f)
                ? profile.TargetWorldLength
                : GetDefaultWeaponLength(item);

            targetWorldLen *= Mathf.Max(unitScale, 1e-3f);
            float scaleMult = (profile != null && profile.ScaleMultiplier > 0.01f) ? profile.ScaleMultiplier : 1.0f;
            targetWorldLen *= scaleMult;

            float parentScale = 1f;
            if (socket != null)
            {
                Vector3 lossy = socket.lossyScale;
                float avg = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
                if (avg > 1e-4f) parentScale = avg;
            }

            float desiredLocalScale = (targetWorldLen / Mathf.Max(baseLen, 1e-4f)) / parentScale;
            return Vector3.one * desiredLocalScale;
        }
    }
}
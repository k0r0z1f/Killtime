using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Core.Character;

namespace Killtime.Core.Inventory
{
    /// <summary>
    /// Armurerie Killtime sous JSON éditable (Livre VIII, marché complet).
    /// Fichier : Assets/Resources/Data/Armory.json ("Data/Armory" en Resources).
    /// Éditable à la main (UTF-8) ou via la fenêtre Armurerie (F12).
    /// Au runtime, un override JSON dans persistentDataPath/Armory.json (sauvegardé
    /// depuis la fenêtre) prend le pas sur le Resources embarqué ; à défaut, le
    /// catalogue codé en dur (<see cref="ArmoryCatalog"/>) sert de repli.
    /// Conventions : enums en NOMS exacts (Weapon, Ballistique, MainHand, Courant...),
    /// statuts grenade en CSV (EnFeu,Saignement...), poids en kg, portée en cases.
    /// </summary>
    [Serializable]
    public class ArmoryEntry
    {
        public string name = "";
        public string dlph = "";
        public int dmg = 0;
        public float weightKg = 0f;
        public int rangeTiles = 1;
        public int priceCE = 0;
        public string type = "Weapon";
        public string skill = "Ballistique";
        public string slot = "MainHand";
        public string category = "";
        public string placeholder = "";
        public string prefab = "";
        public string desc = "";
        public string rarity = "Courant";
        public int armor = 0;
        public int shield = 0;
        public int heal = 0;
        public int bonusEc = 0;
        public bool stackable = false;
        public int qty = 1;
        public int apMod = 0;
        public bool isGrenade = false;
        public bool isLauncher = false;
        public string era = "";
        public string kind = "";
        public int blast = 0;
        public int dice = 0;
        public int shrapnel = 0;
        public string statuses = "";
        public int zoneTurns = 0;
        public bool launcherCompatible = false;
        public int launcherRangeBonus = 0;
        public int accuracyBonus = 0;
        public int ammoCapacity = 0;
        public string ammoType = "";
        public int reloadCost = 2;
        public bool heavyAmmo = false;
    }

    [Serializable]
    public class ArmoryData
    {
        public int version = 1;
        public string source = "";
        public List<ArmoryEntry> items = new List<ArmoryEntry>();
    }

    /// <summary>
    /// Chargeur + convertisseur + sauvegarde du JSON d'armurerie.
    /// Pur C# testable (seuls Resources.Load / persistentDataPath touchent à Unity).
    /// </summary>
    public static class ArmoryJson
    {
        public const string ResourcesPath = "Data/Armory";
        public const string OverrideFileName = "Armory.json";

        private static string _loadedFrom = "";
        public static string LoadedFrom => _loadedFrom;
        public static void InvalidateCache() { _loadedFrom = ""; }

        /// <summary>Priorité : override disque (éditions en jeu) > Resources embarqué.</summary>
        /// <returns>Faux si aucune source ne fournit d'items (repli codé en dur).</returns>
        public static bool TryLoadAll(out List<InventoryItem> items)
        {
            items = null;

            // 1. Override édité en jeu (persistentDataPath).
            try
            {
                string overridePath = Path.Combine(Application.persistentDataPath, OverrideFileName);
                if (File.Exists(overridePath))
                {
                    string json = File.ReadAllText(overridePath);
                    if (TryParse(json, out items, out _))
                    {
                        _loadedFrom = "override:" + overridePath;
                        return true;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Armory] Override illisible : {e.Message}"); }

            // 2. Resources embarqué (édition à la main dans Assets/Resources/Data/Armory.json).
            try
            {
                var asset = Resources.Load<TextAsset>(ResourcesPath);
                if (asset != null && !string.IsNullOrEmpty(asset.text))
                {
                    if (TryParse(asset.text, out items, out _))
                    {
                        _loadedFrom = "resources:" + ResourcesPath;
                        return true;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Armory] Resources illisible : {e.Message}"); }

            _loadedFrom = "";
            items = null;
            return false;
        }

        /// <summary>Parse + convertit un JSON d'armurerie (testable sans Unity).</summary>
        public static bool TryParse(string json, out List<InventoryItem> items, out string error)
        {
            items = null;
            error = "";
            ArmoryData data = null;
            try { data = JsonUtility.FromJson<ArmoryData>(json); }
            catch (Exception e) { error = $"JSON invalide : {e.Message}"; return false; }
            if (data == null || data.items == null || data.items.Count == 0)
            {
                error = "JSON vide : aucun item.";
                return false;
            }
            var list = new List<InventoryItem>(data.items.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < data.items.Count; i++)
            {
                var e = data.items[i];
                if (e == null) { error = $"Item #{i} nul."; return false; }
                if (!TryToItem(e, out var item, out string itemErr))
                {
                    error = $"Item '{(string.IsNullOrEmpty(e.name) ? "#" + i : e.name)}' : {itemErr}";
                    return false;
                }
                if (!seen.Add(item.Name)) { error = $"Doublon : '{item.Name}'."; return false; }
                list.Add(item);
            }
            items = list;
            return true;
        }

        public static bool TryToItem(ArmoryEntry e, out InventoryItem item, out string error)
        {
            item = null;
            error = "";
            if (string.IsNullOrWhiteSpace(e.name)) { error = "nom vide."; return false; }
            if (!Enum.TryParse(e.type, out ItemType type)) { error = $"type inconnu '{e.type}' (Weapon, Ammunition, Consumable, Armor, Arcanotech, Misc)."; return false; }
            if (!Enum.TryParse(e.skill, out SkillType skill)) { error = $"compétence inconnue '{e.skill}'."; return false; }
            if (!Enum.TryParse(e.slot, out ItemEquipSlot slot)) { error = $"slot inconnu '{e.slot}' (None, MainHand, OffHand, TwoHands, Holster)."; return false; }
            if (!Enum.TryParse(e.rarity, out ItemRarity rarity)) { error = $"rareté inconnue '{e.rarity}' (Courant, Militaire, Elite, Legendaire, Prototype)."; return false; }
            if (e.dmg < 0) { error = "dmg négatif."; return false; }
            if (e.priceCE < 0) { error = "priceCE négatif."; return false; }
            if (e.weightKg < 0) { error = "weightKg négatif."; return false; }
            if (e.rangeTiles < 1 || e.rangeTiles > ArmoryCatalog.MaxTacticalRange) { error = $"rangeTiles hors 1-{ArmoryCatalog.MaxTacticalRange}."; return false; }
            if (e.qty < 1) { error = "qty < 1."; return false; }
            if (e.armor < 0 || e.shield < 0 || e.heal < 0 || e.bonusEc < 0) { error = "armor/shield/heal/bonusEc négatif."; return false; }
            if (e.apMod < -5 || e.apMod > 5) { error = "apMod hors -5..5."; return false; }
            if (e.blast < 0 || e.dice < 0 || e.shrapnel < 0 || e.zoneTurns < 0 || e.launcherRangeBonus < 0) { error = "champ grenade/lanceur négatif."; return false; }
            if (e.accuracyBonus < -5 || e.accuracyBonus > 5) { error = "accuracyBonus hors -5..5."; return false; }
            if (e.isGrenade && e.category != "Grenades") { error = "isGrenade exige category Grenades."; return false; }
            if (e.isLauncher && e.category != "Lance-Grenades") { error = "isLauncher exige category Lance-Grenades."; return false; }
            if (e.ammoCapacity < 0 || e.ammoCapacity > 30) { error = "ammoCapacity hors 0..30."; return false; }
            if (e.reloadCost < 1 || e.reloadCost > 4) { error = "reloadCost hors 1..4."; return false; }
            if (e.ammoCapacity > 0 && string.IsNullOrWhiteSpace(e.ammoType)) { error = "ammoCapacity sans ammoType."; return false; }

            string desc = string.IsNullOrEmpty(e.desc) ? "" : e.desc;
            item = new InventoryItem
            {
                ItemId = Guid.NewGuid().ToString("N"),
                Name = e.name.Trim(),
                DlphCode = e.dlph ?? "",
                BaseDamage = e.dmg,
                WeightKg = (float)Math.Round(e.weightKg, 2),
                RangeInTiles = e.rangeTiles,
                PriceCE = e.priceCE,
                Type = type,
                AssociatedSkill = skill,
                EquipSlot = slot,
                Category = string.IsNullOrEmpty(e.category) ? "Divers & Quête" : e.category,
                PlaceholderKind = e.placeholder ?? "",
                PrefabPath = e.prefab ?? "",
                Description = BuildDescription(e, desc),
                Rarity = rarity,
                ArmorProtection = e.armor,
                ShieldHP = e.shield,
                HealingAmount = e.heal,
                AttackBonusEc = e.bonusEc,
                IsStackable = e.stackable,
                Quantity = e.qty,
                ApCostModifier = e.apMod,
                IsEquipped = false,
                IsGrenade = e.isGrenade,
                IsLauncher = e.isLauncher,
                Era = e.era ?? "",
                GrenadeKind = e.kind ?? "",
                BlastRadius = e.blast,
                DamageDiceCount = e.dice,
                ShrapnelDamage = e.shrapnel,
                GrenadeStatuses = e.statuses ?? "",
                ZoneDurationTurns = e.zoneTurns,
                LauncherCompatible = e.launcherCompatible,
                LauncherRangeBonus = e.launcherRangeBonus,
                AccuracyBonus = e.accuracyBonus,
                AmmoCapacity = e.ammoCapacity,
                AmmoType = e.ammoType ?? "",
                AmmoRemaining = e.ammoCapacity,
                LoadedHD = false,
                HeavyAmmo = e.heavyAmmo,
                ReloadAPCost = Math.Clamp(e.reloadCost, 1, 4),
                Jammed = false
            };
            return true;
        }

        /// <summary>Suffixe auto DLPH affiché en boutique (identique au catalogue codé).</summary>
        public static string BuildDescription(ArmoryEntry e, string desc)
        {
            string dlph = e.dlph ?? "";
            if (e.isLauncher)
            {
                string acc = e.accuracyBonus >= 0 ? "+" + e.accuracyBonus : e.accuracyBonus.ToString();
                return $"{desc} [{dlph} — {e.priceCE} CE — Lance-grenades : +{e.launcherRangeBonus} cases, précision {acc}]";
            }
            if (e.isGrenade || e.category == "Grenades")
            {
                return $"{desc} [{dlph} — {e.priceCE} CE — {e.era} / {e.kind} — Blast {e.blast} — Shrap {e.shrapnel}]";
            }
            return $"{desc} [{dlph} — {e.priceCE} CE]";
        }

        /// <summary>Fluff sans le suffixe auto (pour l'éditeur : évite les doublons).</summary>
        public static string BaseDesc(InventoryItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Description)) return "";
            string suffix;
            if (item.IsLauncher)
            {
                string acc = item.AccuracyBonus >= 0 ? "+" + item.AccuracyBonus : item.AccuracyBonus.ToString();
                suffix = $" [{item.DlphCode} — {item.PriceCE} CE — Lance-grenades : +{item.LauncherRangeBonus} cases, précision {acc}]";
            }
            else if (item.IsGrenade || item.Category == "Grenades")
            {
                suffix = $" [{item.DlphCode} — {item.PriceCE} CE — {item.Era} / {item.GrenadeKind} — Blast {item.BlastRadius} — Shrap {item.ShrapnelDamage}]";
            }
            else
            {
                suffix = $" [{item.DlphCode} — {item.PriceCE} CE]";
            }
            if (!string.IsNullOrEmpty(suffix) && item.Description.EndsWith(suffix, StringComparison.Ordinal))
                return item.Description.Substring(0, item.Description.Length - suffix.Length);
            return item.Description;
        }

        /// <summary>Recalcule le suffixe DLPH depuis les champs (édition live).</summary>
        public static void RefreshDescription(InventoryItem item)
        {
            if (item == null) return;
            var e = ToEntry(item);
            if (e == null) return;
            item.Description = BuildDescription(e, e.desc);
        }

        public static ArmoryEntry ToEntry(InventoryItem item)
        {
            if (item == null) return null;
            return new ArmoryEntry
            {
                name = item.Name ?? "",
                dlph = item.DlphCode ?? "",
                dmg = item.BaseDamage,
                weightKg = (float)Math.Round(item.WeightKg, 2),
                rangeTiles = Math.Max(1, item.RangeInTiles),
                priceCE = Math.Max(0, item.PriceCE),
                type = item.Type.ToString(),
                skill = item.AssociatedSkill.ToString(),
                slot = item.EquipSlot.ToString(),
                category = item.Category ?? "",
                placeholder = item.PlaceholderKind ?? "",
                prefab = item.PrefabPath ?? "",
                desc = BaseDesc(item),
                rarity = item.Rarity.ToString(),
                armor = Math.Max(0, item.ArmorProtection),
                shield = Math.Max(0, item.ShieldHP),
                heal = Math.Max(0, item.HealingAmount),
                bonusEc = Math.Max(0, item.AttackBonusEc),
                stackable = item.IsStackable,
                qty = Math.Max(1, item.Quantity),
                apMod = item.ApCostModifier,
                isGrenade = item.IsGrenade,
                isLauncher = item.IsLauncher,
                era = item.Era ?? "",
                kind = item.GrenadeKind ?? "",
                blast = Math.Max(0, item.BlastRadius),
                dice = Math.Max(0, item.DamageDiceCount),
                shrapnel = Math.Max(0, item.ShrapnelDamage),
                statuses = item.GrenadeStatuses ?? "",
                zoneTurns = Math.Max(0, item.ZoneDurationTurns),
                launcherCompatible = item.LauncherCompatible,
                launcherRangeBonus = Math.Max(0, item.LauncherRangeBonus),
                accuracyBonus = item.AccuracyBonus,
                ammoCapacity = Math.Max(0, item.AmmoCapacity),
                ammoType = item.AmmoType ?? "",
                reloadCost = Math.Clamp(item.ReloadAPCost, 1, 4),
                heavyAmmo = item.HeavyAmmo
            };
        }

        /// <summary>Valide tout le catalogue live (noms uniques, enums, plages, catégories).</summary>
        public static bool ValidateAll(List<InventoryItem> list, out string message)
        {
            message = "";
            if (list == null || list.Count == 0) { message = "Catalogue vide."; return false; }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it == null) { message = $"Item #{i} nul."; return false; }
                if (string.IsNullOrWhiteSpace(it.Name)) { message = $"Item #{i} sans nom."; return false; }
                if (!seen.Add(it.Name)) { message = $"Doublon : '{it.Name}'."; return false; }
                if (Array.IndexOf(ArmoryCatalog.Categories, it.Category) < 0)
                {
                    message = $"'{it.Name}' : catégorie inconnue '{it.Category}'.";
                    return false;
                }
                if (it.PriceCE < 0 || it.BaseDamage < 0 || it.WeightKg < 0)
                {
                    message = $"'{it.Name}' : prix/dégâts/poids négatif.";
                    return false;
                }
                if (it.RangeInTiles < 1 || it.RangeInTiles > ArmoryCatalog.MaxTacticalRange)
                {
                    message = $"'{it.Name}' : portée hors 1-{ArmoryCatalog.MaxTacticalRange}.";
                    return false;
                }
                // Cohérence exigée au rechargement (TryToItem) : la sauvegarde la garantit
                // pour éviter un override qui retomberait silencieusement sur l'ancien JSON.
                if (it.IsGrenade && it.Category != "Grenades")
                {
                    message = $"'{it.Name}' : grenade hors catégorie Grenades (décochez IsGrenade ou remettez la catégorie).";
                    return false;
                }
                if (it.IsLauncher && it.Category != "Lance-Grenades")
                {
                    message = $"'{it.Name}' : lanceur hors catégorie Lance-Grenades (décochez IsLauncher ou remettez la catégorie).";
                    return false;
                }
                if (it.AmmoCapacity > 0 && string.IsNullOrWhiteSpace(it.AmmoType))
                {
                    message = $"'{it.Name}' : chargeur sans type de munition.";
                    return false;
                }
                if (string.IsNullOrEmpty(it.PlaceholderKind) && string.IsNullOrEmpty(it.PrefabPath))
                    warnings.Add($"'{it.Name}' : ni placeholder ni prefab (repli Generic).");
            }
            message = $"✓ Catalogue valide : {list.Count} items, {ArmoryCatalog.Categories.Length} catégories."
                + (warnings.Count > 0 ? $" ⚠ {warnings.Count} avertissement(s) : {string.Join(" | ", warnings.GetRange(0, Math.Min(3, warnings.Count)))}" : "");
            return true;
        }

        public static string GetOverridePath()
        {
            return Path.Combine(Application.persistentDataPath, OverrideFileName);
        }

        /// <summary>Sérialise le catalogue live vers l'override disque (rechargé en priorité).</summary>
        public static bool SaveOverride(List<InventoryItem> list, out string message)
        {
            message = "";
            try
            {
                var data = new ArmoryData
                {
                    version = 1,
                    source = "Édité en jeu (fenêtre Armurerie) — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    items = new List<ArmoryEntry>()
                };
                foreach (var it in list)
                {
                    var e = ToEntry(it);
                    if (e != null) data.items.Add(e);
                }
                if (!ValidateAll(list, out string vmsg))
                {
                    message = "Sauvegarde refusée : " + vmsg;
                    return false;
                }
                File.WriteAllText(GetOverridePath(), JsonUtility.ToJson(data, true));
                message = $"Armurerie sauvegardée : {GetOverridePath()} ({data.items.Count} items). Rechargée. {vmsg}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Échec sauvegarde : {ex.Message}";
                return false;
            }
        }

#if UNITY_EDITOR
        /// <summary>Éditeur Unity uniquement : réexporte vers le Resources embarqué.</summary>
        public static bool ExportToResources(List<InventoryItem> list, out string message)
        {
            message = "";
            try
            {
                var data = new ArmoryData
                {
                    version = 1,
                    source = "Export éditeur (fenêtre Armurerie) — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    items = new List<ArmoryEntry>()
                };
                foreach (var it in list)
                {
                    var e = ToEntry(it);
                    if (e != null) data.items.Add(e);
                }
                if (!ValidateAll(list, out string vmsg))
                {
                    message = "Export refusé : " + vmsg;
                    return false;
                }
                string path = Path.Combine(Application.dataPath, "Resources/Data/Armory.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(data, true));
                UnityEditor.AssetDatabase.Refresh();
                message = $"Exporté vers {path} ({data.items.Count} items). {vmsg}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Échec export : {ex.Message}";
                return false;
            }
        }
#endif
    }
}

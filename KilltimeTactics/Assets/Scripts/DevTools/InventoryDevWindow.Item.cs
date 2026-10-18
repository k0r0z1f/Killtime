// InventoryDevWindow.Item.cs — split move-only de InventoryDevWindow.cs (propriétés, icônes, fiche).
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    public partial class InventoryDevWindow
    {

        // ================= ONGLET 3 : PROPRIÉTÉS =================
        private void DrawItemPropertiesTab()
        {
            if (_inspectedItem == null)
            {
                GUILayout.Label("<i>Sélectionnez un objet dans le sac pour modifier ses propriétés.</i>");
                if (_previewCatalogDef != null)
                    GUILayout.Label($"<i>Aperçu catalogue : {_previewCatalogDef.Name} — utilisez + Sac / Acheter pour l'obtenir puis éditez-le.</i>");
                return;
            }

            GUILayout.Label($"<b>Édition : {_inspectedItem.Name}</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(90));
            _inspectedItem.Name = GUILayout.TextField(_inspectedItem.Name);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Dégâts Bruts :", GUILayout.Width(90));
            _inspectedItem.BaseDamage = (int)GUILayout.HorizontalSlider(_inspectedItem.BaseDamage, 0, 25);
            GUILayout.Label($"{_inspectedItem.BaseDamage}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Portée (Cases) :", GUILayout.Width(90));
            _inspectedItem.RangeInTiles = (int)GUILayout.HorizontalSlider(_inspectedItem.RangeInTiles, 1, 20);
            GUILayout.Label($"{_inspectedItem.RangeInTiles}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Poids (Kg) :", GUILayout.Width(90));
            _inspectedItem.WeightKg = GUILayout.HorizontalSlider(_inspectedItem.WeightKg, 0.05f, 20.0f);
            GUILayout.Label($"{_inspectedItem.WeightKg:0.#}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Prix (CE) :", GUILayout.Width(90));
            _inspectedItem.PriceCE = (int)GUILayout.HorizontalSlider(_inspectedItem.PriceCE, 0, 250000);
            GUILayout.Label($"{_inspectedItem.PriceCE}", GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Armure :", GUILayout.Width(90));
            _inspectedItem.ArmorProtection = (int)GUILayout.HorizontalSlider(_inspectedItem.ArmorProtection, 0, 10);
            GUILayout.Label($"{_inspectedItem.ArmorProtection}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Soin PV :", GUILayout.Width(90));
            _inspectedItem.HealingAmount = (int)GUILayout.HorizontalSlider(_inspectedItem.HealingAmount, 0, 30);
            GUILayout.Label($"{_inspectedItem.HealingAmount}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.Label("Description :");
            _inspectedItem.Description = GUILayout.TextArea(_inspectedItem.Description, GUILayout.Height(50));

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
            if (GUILayout.Button("💾 Sauvegarder sur Disque", GUILayout.Height(28)))
            {
                var sheet = GetActiveSheet();
                SaveAndSync(sheet);
                _statusMessage = $"Propriétés de '{_inspectedItem.Name}' enregistrées.";
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
        }

        private static string IconFor(InventoryItem item)
        {
            if (item == null) return "📦";
            string nm = item.Name ?? "";
            string cat = item.Category ?? "";
            switch (item.Type)
            {
                case ItemType.Weapon:
                    if (cat.Contains("Laser") && (nm.Contains("Épée") || nm.Contains("Sabre") || nm.Contains("Espadon"))) return "🔷";
                    if (cat == "Lance-Grenades" || (item.IsLauncher)) return "🚀";
                    if (item.RangeInTiles > 2) return "🔫";
                    if (cat == "Grenades" || item.IsGrenade) return "💣";
                    return "🗡️";
                case ItemType.Ammunition: return "📦";
                case ItemType.Armor: return "🛡️";
                case ItemType.Arcanotech: return "🔮";
                case ItemType.Consumable: return item.HealingAmount > 0 ? "💉" : "💊";
                default: return "🎒";
            }
        }

        private void DrawItemDetailsCard()
        {
            var item = GetCurrentPreviewItem();
            if (item != null)
            {
                GUI.color = Color.white;
                GUILayout.Label($"<b>Fiche : <color=#00E5FF>{item.Name}</color></b>", RichLabel());
                GUILayout.Label($"<color=#9FB3C8>{item.Category} • {item.Type} • {item.Rarity} • {item.DlphCode}</color>", RichLabel());
                string stats = $"Dégâts <b><color=#FF3B5C>{item.BaseDamage}</color></b> • Portée <b>{item.RangeInTiles}</b> • <b>{item.WeightKg:0.#} kg</b> • <b><color=#FFD75F>{item.PriceCE} CE</color></b>";
                if (item.ArmorProtection > 0) stats += $" • 🛡️+{item.ArmorProtection}";
                if (item.ShieldHP > 0) stats += $" • 🔮{item.ShieldHP} PV";
                if (item.HealingAmount > 0) stats += $" • 💉+{item.HealingAmount} PV";
                if (item.AttackBonusEc != 0) stats += $" • +{item.AttackBonusEc}ec";
                if (item.ApCostModifier != 0) stats += $" • {item.ApCostModifier:+0;-0} PA";
                if (item.AmmoCapacity > 0) stats += $" • 🔋{item.AmmoRemaining}/{item.AmmoCapacity} ({item.AmmoType}{(item.LoadedHD ? " HD+1D" : "")}, reload {item.ReloadAPCost} PA){(item.Jammed ? " <color=red>ENRAYÉE</color>" : "")}";
                GUILayout.Label(stats, RichLabel());
                GUILayout.Label($"Compétence <b>{item.AssociatedSkill}</b> • Slot <b>{item.EquipSlot}</b> • {(item.IsEquipped ? "<color=#00FF88>Équipé</color>" : "En réserve")}", RichLabel());
                if (item.IsGrenade)
                {
                    string gLine = $"💣 <b>{item.Era} / {item.GrenadeKind}</b> • Souffle R<b>{item.BlastRadius}</b> • <b>{item.BaseDamage}+{item.DamageDiceCount}d10</b> + shrap <b>{item.ShrapnelDamage}</b>";
                    if (!string.IsNullOrEmpty(item.GrenadeStatuses)) gLine += $" • [{item.GrenadeStatuses}]";
                    if (item.ZoneDurationTurns > 0) gLine += $" • Zone {item.ZoneDurationTurns} tours";
                    gLine += item.LauncherCompatible ? " • <color=#00E5FF>Compatible lanceur</color>" : " • <i>Main uniquement</i>";
                    if (item.AccuracyBonus != 0) gLine += $" • Précision {item.AccuracyBonus:+0;-0}";
                    GUILayout.Label(gLine, RichLabel());
                }
                if (item.IsLauncher)
                {
                    GUILayout.Label($"🚀 Lance-grenades : <b>+{item.LauncherRangeBonus} cases</b> (max 20) • Précision <b>{item.AccuracyBonus:+0;-0}</b> • Tir 3 PA", RichLabel());
                }
                GUILayout.Label($"<i>{item.Description}</i>", RichLabel());

                // Actions rapides contextuelles
                if (_selectedTab != 0 && GetActiveSheet() != null)
                {
                    GUILayout.BeginHorizontal();
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                    if (GUILayout.Button("+ Sac (dotation)", GUILayout.Height(26))) GiveCatalogItemToUnit(item);
                    GUI.backgroundColor = new Color(0.95f, 0.75f, 0.15f);
                    var sheet = GetActiveSheet();
                    GUI.enabled = sheet.CanAfford(item.PriceCE);
                    if (GUILayout.Button($"Acheter {item.PriceCE} CE", GUILayout.Height(26)))
                    {
                        ArmoryCatalog.BuyForSheet(sheet, item.Name, out string msg);
                        SaveAndSync(sheet);
                        _statusMessage = msg;
                    }
                    GUI.enabled = true;
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("<color=gray><i>Sélectionnez un objet pour voir sa fiche et son modèle 3D.</i></color>");
            }
        }
    }
}

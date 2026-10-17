using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Inventory;

namespace Killtime.UI
{
    /// <summary>
    /// Fenêtre Armurerie (F12) — visualise et édite TOUT le marketplace Livre VIII.
    /// Source : ArmoryCatalog (Resources/Data/Armory.json, override persistentDataPath).
    /// Tout est éditable : identité, prix, dégâts, portée, protections, soins, grenades.
    /// Sauvegarde → override disque (prioritaire au reload). Export → Resources (éditeur).
    /// Le marché (F4) et le combat lisent la liste live : toute édition est immédiate.
    /// </summary>
    public class ArmoryDevWindow : FloatingWindow<ArmoryDevWindow>
    {
        protected override int WindowId => 897;
        protected override string Title => "Armurerie JSON — Catalogue & Marché (F12)";
        protected override Vector2 MinSize => new Vector2(620, 340);
        protected override Rect DefaultRect => new Rect(40f, 92f, 760f, Mathf.Min(740f, Screen.height - 110f));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F12 };

        private static readonly string[] Types = { "Weapon", "Ammunition", "Consumable", "Armor", "Arcanotech", "Misc" };
        private static readonly string[] Slots = { "None", "MainHand", "OffHand", "TwoHands", "Holster" };
        private static readonly string[] Rarities = { "Courant", "Militaire", "Elite", "Legendaire", "Prototype" };
        private static readonly string[] Skills =
        {
            "MainsNues", "ManiementArmes", "ArmesPercantes", "DefenseCorporelle", "Athletisme",
            "Ballistique", "Esquive", "Acrobatie", "Discretion", "ConduitePilotage", "Subterfuge",
            "EndurancePhysique", "Cardio", "SystemeImmunitaire", "Academie", "PremiersSoins",
            "MedecineAvancee", "IngenierieArcanotech", "TactiqueStrategie", "Communication",
            "Intimidation", "Leadership", "Observation", "Ecoute", "Intuition", "NatureSurvie",
            "Artisanat", "MagieElementale", "MagiePrimale", "MagieEsprit"
        };

        private string _eraFilter = "Toutes";
        private string _categoryFilter = "Toutes";
        private string _search = "";
        private List<InventoryItem> _visible = new List<InventoryItem>();
        private InventoryItem _selected;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _status = "Prêt.";
        private bool _dirty;
        private bool _suffixDirty;

        protected override void OnOpened() { Refresh(); }

        private void Refresh()
        {
            var all = ArmoryCatalog.All;
            _visible = new List<InventoryItem>();
            string q = (_search ?? "").Trim().ToLowerInvariant();
            foreach (var it in all)
            {
                if (it == null) continue;
                string era = ArmoryCatalog.DeduceEra(it);
                if (_eraFilter != "Toutes" && era != _eraFilter) continue;
                if (_categoryFilter != "Toutes" && it.Category != _categoryFilter) continue;
                if (!string.IsNullOrEmpty(q) && (it.Name ?? "").ToLowerInvariant().Contains(q) == false && !era.ToLowerInvariant().Contains(q)) continue;
                _visible.Add(it);
            }

            _visible.Sort((a, b) =>
            {
                int eraA = Array.IndexOf(ArmoryCatalog.Eras, ArmoryCatalog.DeduceEra(a));
                int eraB = Array.IndexOf(ArmoryCatalog.Eras, ArmoryCatalog.DeduceEra(b));
                if (eraA != eraB) return eraA.CompareTo(eraB);
                int catCmp = string.Compare(a.Category, b.Category, StringComparison.Ordinal);
                if (catCmp != 0) return catCmp;
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            if (_selected != null && !all.Contains(_selected)) _selected = null;
            if (_selected == null && _visible.Count > 0) _selected = _visible[0];
        }

        protected override void DrawContent()
        {
            GUILayout.Label($"Source : {ArmoryCatalog.LoadedFrom} · {ArmoryCatalog.All.Count} items{(_dirty ? " · MODIFIÉ (non sauvegardé)" : "")}",
                new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Italic });

            // ---- Filtre Ères Technologiques ----
            var eras = new List<string> { "Toutes" };
            eras.AddRange(ArmoryCatalog.Eras);
            int selEra = Math.Max(0, eras.IndexOf(_eraFilter));
            int newEra = GUILayout.SelectionGrid(selEra, eras.ToArray(), 4);
            if (newEra != selEra) { _eraFilter = eras[newEra]; Refresh(); }

            // ---- Filtres catégories ----
            var cats = new List<string> { "Toutes" };
            cats.AddRange(ArmoryCatalog.Categories);
            int selCat = Math.Max(0, cats.IndexOf(_categoryFilter));
            int newCat = GUILayout.SelectionGrid(selCat, cats.ToArray(), 4);
            if (newCat != selCat) { _categoryFilter = cats[newCat]; Refresh(); }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Recherche", GUILayout.Width(70));
            string nq = GUILayout.TextField(_search ?? "");
            if (nq != _search) { _search = nq; Refresh(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // ---- Liste ----
            GUILayout.BeginVertical(GUILayout.Width(250));
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _visible.Count; i++)
            {
                var it = _visible[i];
                if (it == null) continue;
                bool sel = _selected == it;
                string tag = it.IsGrenade ? "💣" : (it.IsLauncher ? "🚀" : (it.Type == ItemType.Weapon ? "⚔" : (it.HealingAmount > 0 ? "💉" : "📦")));
                string label = $"{tag} [{ArmoryCatalog.DeduceEra(it)}] {it.Name} ({it.PriceCE} CE)";
                if (GUILayout.Toggle(sel, label, GUI.skin.button) != sel)
                    _selected = it;
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Nouveau")) CreateEntry();
            if (GUILayout.Button("Dupliquer") && _selected != null) DuplicateSelected();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // ---- Détail / édition ----
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _detailScroll = GUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandHeight(true));
            if (_selected != null) DrawEditor(_selected);
            else GUILayout.Label("Aucun item (filtre vide).");
            GUILayout.EndScrollView();

            if (_suffixDirty && _selected != null)
            {
                ArmoryJson.RefreshDescription(_selected);
                _suffixDirty = false;
            }

            // ---- Barre d'actions ----
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Recharger JSON")) { ArmoryCatalog.InvalidateCache(); Refresh(); _dirty = false; _status = "Rechargé : " + ArmoryCatalog.LoadedFrom; }
            if (GUILayout.Button("Valider")) { ArmoryJson.ValidateAll(ArmoryCatalog.All, out string v); _status = v; }
            if (GUILayout.Button("Sauvegarder")) { bool ok = ArmoryCatalog.SaveCatalog(out string m); _status = m; if (ok) _dirty = false; Refresh(); }
#if UNITY_EDITOR
            if (GUILayout.Button("Exporter Resources")) { bool ok = ArmoryCatalog.ExportCatalogToResources(out string m); _status = m; if (ok) _dirty = false; Refresh(); }
#endif
            if (GUILayout.Button("Marché (F4)")) InventoryDevWindow.Open();
            if (_selected != null && GUILayout.Button("Supprimer", GUILayout.Width(90))) DeleteSelected();
            GUILayout.EndHorizontal();

            GUILayout.Label(_status, new GUIStyle(GUI.skin.label) { fontSize = 11 });
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        // ==================================================================
        private void DrawEditor(InventoryItem it)
        {
            GUILayout.Label($"FICHE : {it.Name}", new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold });
            bool dirtyBefore = _dirty;

            string nn = TextRow("Nom", it.Name ?? "");
            if (nn != it.Name)
            {
                if (string.IsNullOrWhiteSpace(nn)) _status = "Nom vide refusé.";
                else if (NameTaken(nn, it)) _status = $"Doublon refusé : '{nn}'.";
                else { it.Name = nn.Trim(); _dirty = true; }
            }
            string dlph = TextRow("DLPH", it.DlphCode ?? "");
            if (dlph != it.DlphCode) { it.DlphCode = dlph; _dirty = true; _suffixDirty = true; }
            CycleRow("Ère", ArmoryCatalog.Eras, v => it.Era = v, ArmoryCatalog.DeduceEra(it));
            CycleRow("Catégorie", ArmoryCatalog.Categories, v => it.Category = v, it.Category);
            CycleRow("Type", Types, v => { if (Enum.TryParse(v, out ItemType t)) it.Type = t; }, it.Type.ToString());
            CycleRow("Compétence", Skills, v => { if (Enum.TryParse(v, out SkillType s)) it.AssociatedSkill = s; }, it.AssociatedSkill.ToString());
            CycleRow("Slot", Slots, v => { if (Enum.TryParse(v, out ItemEquipSlot s)) it.EquipSlot = s; }, it.EquipSlot.ToString());
            CycleRow("Rareté", Rarities, v => { if (Enum.TryParse(v, out ItemRarity r)) it.Rarity = r; }, it.Rarity.ToString());
            it.PriceCE = IntRow("Prix CE", it.PriceCE, 0, 999999);

            GUILayout.Label("Combat", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            it.BaseDamage = IntRow("Dégâts", it.BaseDamage, 0, 30);
            it.RangeInTiles = IntRow("Portée (cases)", it.RangeInTiles, 1, ArmoryCatalog.MaxTacticalRange);
            it.AttackBonusEc = IntRow("Bonus EC", it.AttackBonusEc, 0, 5);
            it.ApCostModifier = IntRow("Mod PA", it.ApCostModifier, -5, 5);

            GUILayout.Label("Physique & visuel", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            it.WeightKg = FloatRow("Poids kg", it.WeightKg, 0f, 50f);
            it.PrefabPath = TextRow("Prefab", it.PrefabPath ?? "");
            it.PlaceholderKind = TextRow("Placeholder", it.PlaceholderKind ?? "");
            GUILayout.Label("Kinds : SwordMetal, SwordMetalLong, Club, Axe, Hammer, Spear, Bow, LaserSword, LaserSwordLong, PistolLaser, RifleLaser, SniperLaser, Deglazer, Grenade, GrenadeLauncher, ArmorLight, ArmorHeavy, ShieldGen, Syringe, Pills, Ration, Cell",
                new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });

            GUILayout.Label("Protection & soins", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            it.ArmorProtection = IntRow("Armure", it.ArmorProtection, 0, 10);
            it.ShieldHP = IntRow("Bouclier PV", it.ShieldHP, 0, 60);
            it.HealingAmount = IntRow("Soin PV", it.HealingAmount, 0, 30);
            bool stk = GUILayout.Toggle(it.IsStackable, "Stackable (pile / dose)");
            if (stk != it.IsStackable) { it.IsStackable = stk; _dirty = true; }

            GUILayout.Label("Munitions RD-033 (0 = sans chargeur)", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            it.AmmoCapacity = IntRow("Chargeur", it.AmmoCapacity, 0, 30);
            if (it.AmmoCapacity > 0)
            {
                it.AmmoType = TextRow("Type réserve", it.AmmoType ?? "");
                if (string.IsNullOrWhiteSpace(it.AmmoType)) _status = "Chargeur sans type de munition (ex: Charge Laser).";
                it.ReloadAPCost = IntRow("Reload PA", it.ReloadAPCost, 1, 4);
                bool hv = GUILayout.Toggle(it.HeavyAmmo, "Accepte Haute Densité +1D (sniper/Deglazer)");
                if (hv != it.HeavyAmmo) { it.HeavyAmmo = hv; _dirty = true; }
                it.AmmoRemaining = Math.Clamp(it.AmmoRemaining, 0, Math.Max(0, it.AmmoCapacity));
                GUILayout.Label($"Réserve : Charge Laser = 10 coups/charge, Carquois = 20 flèches. Enrayement sur critique adverse, désenrayer 1 PA.",
                    new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });
            }
            else
            {
                it.AmmoType = "";
                it.LoadedHD = false;
                it.Jammed = false;
            }

            GUILayout.Label("Fluff (suffixe DLPH auto — aperçu ci-dessous)");
            string baseD = ArmoryJson.BaseDesc(it);
            string nd = GUILayout.TextArea(baseD, GUILayout.MinHeight(40));
            if (nd != baseD) { it.Description = nd; _dirty = true; _suffixDirty = true; }
            GUILayout.Label(it.Description ?? "", new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic });

            GUILayout.Label("Grenades & lanceurs", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            bool gr = GUILayout.Toggle(it.IsGrenade, "Est grenade (zone, catégorie Grenades requise)");
            if (gr != it.IsGrenade)
            {
                if (gr && it.Category != "Grenades") _status = "Grenade exige la catégorie Grenades.";
                else { it.IsGrenade = gr; _dirty = true; _suffixDirty = true; }
            }
            bool la = GUILayout.Toggle(it.IsLauncher, "Est lance-grenades (catégorie Lance-Grenades requise)");
            if (la != it.IsLauncher)
            {
                if (la && it.Category != "Lance-Grenades") _status = "Lanceur exige la catégorie Lance-Grenades.";
                else { it.IsLauncher = la; _dirty = true; _suffixDirty = true; }
            }
            if (it.IsGrenade || it.Category == "Grenades" || it.IsLauncher)
            {
                it.GrenadeKind = TextRow("Famille", it.GrenadeKind ?? "");
                it.BlastRadius = IntRow("Blast (cases)", it.BlastRadius, 0, 6);
                it.DamageDiceCount = IntRow("Dés d10", it.DamageDiceCount, 0, 6);
                it.ShrapnelDamage = IntRow("Shrapnels", it.ShrapnelDamage, 0, 6);
                it.GrenadeStatuses = TextRow("Statuts CSV", it.GrenadeStatuses ?? "");
                it.ZoneDurationTurns = IntRow("Zone (tours)", it.ZoneDurationTurns, 0, 5);
                bool lc = GUILayout.Toggle(it.LauncherCompatible, "Compatible lanceur");
                if (lc != it.LauncherCompatible) { it.LauncherCompatible = lc; _dirty = true; }
                it.LauncherRangeBonus = IntRow("+Portée lanceur", it.LauncherRangeBonus, 0, 15);
                it.AccuracyBonus = IntRow("Précision", it.AccuracyBonus, -5, 5);
            }

            // Tout changement de champ régénère le suffixe DLPH (idempotent).
            if (_dirty != dirtyBefore) _suffixDirty = true;
        }

        private bool NameTaken(string name, InventoryItem self)
        {
            foreach (var it in ArmoryCatalog.All)
                if (it != null && it != self && string.Equals(it.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private string TextRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            string nv = GUILayout.TextField(value ?? "");
            GUILayout.EndHorizontal();
            if (nv != value) _dirty = true;
            return nv;
        }

        private int IntRow(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            if (GUILayout.Button("-", GUILayout.Width(26))) { value = Math.Max(min, value - 1); _dirty = true; }
            GUILayout.Label(value.ToString(), GUILayout.Width(56));
            if (GUILayout.Button("+", GUILayout.Width(26))) { value = Math.Min(max, value + 1); _dirty = true; }
            GUILayout.EndHorizontal();
            return Math.Clamp(value, min, max);
        }

        private float FloatRow(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            if (GUILayout.Button("-", GUILayout.Width(26))) { value = Mathf.Max(min, value - 0.05f); _dirty = true; }
            string txt = GUILayout.TextField(value.ToString("0.##"), GUILayout.Width(56));
            if (float.TryParse(txt, out float pv)) { pv = Mathf.Clamp(pv, min, max); if (Math.Abs(pv - value) > 0.0001f) { value = pv; _dirty = true; } }
            if (GUILayout.Button("+", GUILayout.Width(26))) { value = Mathf.Min(max, value + 0.05f); _dirty = true; }
            GUILayout.EndHorizontal();
            return Mathf.Clamp((float)Math.Round(value, 2), min, max);
        }

        private void CycleRow(string label, string[] options, Action<string> set, string current)
        {
            int idx = Array.IndexOf(options, current);
            if (idx < 0) idx = 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            if (GUILayout.Button("◀", GUILayout.Width(30))) { idx = (idx - 1 + options.Length) % options.Length; set(options[idx]); _dirty = true; }
            GUILayout.Label(options[idx], GUILayout.Width(170));
            if (GUILayout.Button("▶", GUILayout.Width(30))) { idx = (idx + 1) % options.Length; set(options[idx]); _dirty = true; }
            GUILayout.EndHorizontal();
        }

        // ==================================================================
        private void CreateEntry()
        {
            var all = ArmoryCatalog.All;
            int n = all.Count + 1;
            string name = "Nouvel Objet " + n;
            while (NameTaken(name, null)) { n++; name = "Nouvel Objet " + n; }
            var it = new InventoryItem
            {
                Name = name,
                DlphCode = "—",
                BaseDamage = 0,
                WeightKg = 0.45f,
                RangeInTiles = 1,
                PriceCE = 100,
                Type = ItemType.Misc,
                AssociatedSkill = SkillType.Academie,
                EquipSlot = ItemEquipSlot.None,
                Era = _eraFilter != "Toutes" ? _eraFilter : "Moderne",
                Category = _categoryFilter != "Toutes" ? _categoryFilter : "Divers & Quête",
                PlaceholderKind = "Cell",
                Description = "À décrire. [— — 100 CE]"
            };
            all.Add(it);
            _selected = it;
            _dirty = true;
            Refresh();
            _status = "Nouvel item créé (pensez à sauvegarder).";
        }

        private void DuplicateSelected()
        {
            if (_selected == null) return;
            var copy = _selected.Clone();
            string baseName = _selected.Name + " (copie)";
            string name = baseName;
            int k = 2;
            while (NameTaken(name, null)) { name = $"{baseName} {k}"; k++; }
            copy.Name = name;
            copy.IsEquipped = false;
            ArmoryCatalog.All.Add(copy);
            _selected = copy;
            _dirty = true;
            Refresh();
        }

        private void DeleteSelected()
        {
            if (_selected == null) return;
            ArmoryCatalog.All.Remove(_selected);
            _status = $"Item '{_selected.Name}' supprimé (pensez à sauvegarder).";
            _selected = null;
            _dirty = true;
            Refresh();
        }
    }
}

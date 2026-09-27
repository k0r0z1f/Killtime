using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    /// <summary>
    /// Fenêtre Bestiaire (touche B) — visualise, édite et spawne le bestiaire JSON.
    /// Source : BestiaryCatalog (Resources/Data/Bestiary.json, override persistentDataPath).
    /// Tout est éditable : identité, attributs, skills, spécialisations, équipement.
    /// Sauvegarde → override disque (prioritaire au reload). Export → Resources (éditeur).
    /// </summary>
    public class BestiaryDevWindow : FloatingWindow<BestiaryDevWindow>
    {
        protected override int WindowId => 889;
        protected override string Title => "Bestiaire — Créatures & PNJ";
        protected override Vector2 MinSize => new Vector2(560, 320);
        protected override Rect DefaultRect => new Rect(40f, 92f, 700f, Mathf.Min(720f, Screen.height - 110f));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.B };

        private static readonly string[] Categories = { "Toutes", "Minion", "Base", "Civil", "Bandit", "Elite", "Faune", "Automate", "Boss", "Build" };
        private static readonly string[] Profiles = { "PnjSbire", "PnjNormal", "PnjBoss", "HerosPJ" };
        private static readonly string[] Species = { "Humain", "Nain", "Taurien", "Cleien", "Mikyai", "Vardien" };
        private static readonly string[] Footprints = { "Single", "Triangle3", "Rosette7", "Colossus19" };
        private static readonly string[] Ranks = { "Rang I", "Rang II", "Rang III" };

        private string _filter = "Toutes";
        private List<BestiaryEntry> _visible = new List<BestiaryEntry>();
        private BestiaryEntry _selected;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _status = "Prêt.";
        private bool _dirty;

        private string _newSkillName = "MainsNues";
        private string _newSpecName = "";
        private string _newEquipName = "";
        private int _spawnQ = 0;
        private int _spawnR = 1;

        protected override void OnOpened() { Refresh(); }

        private void Refresh()
        {
            _visible = BestiaryCatalog.GetByCategory(_filter);
            if (_selected != null)
                _selected = BestiaryCatalog.GetById(_selected.id) ?? _selected;
            if (_selected == null && _visible.Count > 0) _selected = _visible[0];
        }

        protected override void DrawContent()
        {
            GUILayout.Label($"Source : {BestiaryCatalog.LoadedFrom} · {BestiaryCatalog.Count} entrées{(_dirty ? " · MODIFIÉ (non sauvegardé)" : "")}",
                new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Italic });

            // ---- Filtres ----
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Categories.Length; i++)
            {
                bool active = _filter == Categories[i];
                if (GUILayout.Toggle(active, Categories[i], GUI.skin.button) != active)
                {
                    _filter = Categories[i];
                    Refresh();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // ---- Liste ----
            GUILayout.BeginVertical(GUILayout.Width(220));
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _visible.Count; i++)
            {
                var e = _visible[i];
                if (e == null) continue;
                bool sel = _selected == e;
                string label = $"[{e.category}] {e.name}";
                if (GUILayout.Toggle(sel, label, GUI.skin.button) != sel)
                    _selected = e;
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
            else GUILayout.Label("Aucune entrée.");
            GUILayout.EndScrollView();

            // ---- Barre d'actions ----
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Recharger JSON")) { BestiaryCatalog.InvalidateCache(); Refresh(); _dirty = false; _status = "Rechargé depuis le disque."; }
            if (GUILayout.Button("Sauvegarder")) SaveOverride();
#if UNITY_EDITOR
            if (GUILayout.Button("Exporter Resources")) ExportResources();
#endif
            if (_selected != null && GUILayout.Button("Supprimer", GUILayout.Width(90))) DeleteSelected();
            GUILayout.EndHorizontal();

            GUILayout.Label(_status, new GUIStyle(GUI.skin.label) { fontSize = 11 });
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        // ==================================================================
        private void DrawEditor(BestiaryEntry e)
        {
            GUILayout.Label($"FICHE : {e.id}", new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold });

            e.name = TextRow("Nom", e.name);
            e.id = TextRow("ID", e.id);
            CycleRow("Catégorie", Categories, v => e.category = v, e.category);
            CycleRow("Rang", Ranks, v => e.rank = v, e.rank);
            CycleRow("Profil", Profiles, v => e.profile = v, e.profile);
            CycleRow("Espèce", Species, v => e.species = v, e.species);
            CycleRow("Empreinte", Footprints, v => e.footprint = v, e.footprint);
            e.modelPrefab = TextRow("Prefab 3D", e.modelPrefab);
            e.age = IntRow("Âge", e.age, 0, 100000);
            e.gender = TextRow("Genre", e.gender);

            GUILayout.Label("Description");
            e.description = GUILayout.TextArea(e.description ?? "", GUILayout.MinHeight(44));
            GUILayout.Label("Tactique");
            e.tactics = GUILayout.TextArea(e.tactics ?? "", GUILayout.MinHeight(36));

            // ---- Attributs ----
            GUILayout.Label("Attributs (Livre I)", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            var a = e.attributes ?? (e.attributes = new BestiaryAttributes());
            a.FOR = IntRow("FOR", a.FOR, 1, 10); a.AGI = IntRow("AGI", a.AGI, 1, 10);
            a.CON = IntRow("CON", a.CON, 1, 10); a.RAP = IntRow("RAP", a.RAP, 1, 10);
            a.INT = IntRow("INT", a.INT, 1, 10); a.ERU = IntRow("ÉRU", a.ERU, 1, 10);
            a.CHA = IntRow("CHA", a.CHA, 1, 10); a.INS = IntRow("INS", a.INS, 1, 10);
            a.MAG = IntRow("MAG", a.MAG, 0, 10);
            a.Vision = IntRow("Vision", a.Vision, 1, 6); a.Ouie = IntRow("Ouïe", a.Ouie, 1, 6);
            a.Miracle = IntRow("PM", a.Miracle, 0, 5);
            e.armor = IntRow("Armure", e.armor, 0, 10);
            e.credits = IntRow("Crédits CE", e.credits, 0, 999999);

            int pa = Mathf.Max(a.AGI, a.INT) + a.RAP + Mathf.Min(Mathf.Min(Mathf.Min(a.FOR, a.AGI), Mathf.Min(a.CON, a.RAP)), Mathf.Min(Mathf.Min(a.INT, a.ERU), Mathf.Min(a.CHA, a.INS)));
            GUILayout.Label($"Dérivés moteur : {pa} PA · Encaissement {a.CON * 2} · Létal {a.CON * 5}");
            if (BestiaryCatalog.Validate(e, out string vmsg))
                GUILayout.Label("✓ " + vmsg);
            else
                GUILayout.Label("⚠ " + vmsg);

            // ---- Skills ----
            GUILayout.Label("Compétences (nom enum ou affichage, niveau 0-3)", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            if (e.skills == null) e.skills = new List<BestiarySkillEntry>();
            for (int i = e.skills.Count - 1; i >= 0; i--)
            {
                var se = e.skills[i];
                if (se == null) { e.skills.RemoveAt(i); _dirty = true; continue; }
                GUILayout.BeginHorizontal();
                se.skill = GUILayout.TextField(se.skill ?? "", GUILayout.Width(200));
                if (GUILayout.Button("-", GUILayout.Width(26)) && se.level > 0) { se.level--; _dirty = true; }
                GUILayout.Label(se.level.ToString(), GUILayout.Width(20));
                if (GUILayout.Button("+", GUILayout.Width(26)) && se.level < 3) { se.level++; _dirty = true; }
                if (GUILayout.Button("x", GUILayout.Width(26))) { e.skills.RemoveAt(i); _dirty = true; }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            _newSkillName = GUILayout.TextField(_newSkillName, GUILayout.Width(200));
            if (GUILayout.Button("+ Ajouter skill", GUILayout.Width(130)))
            {
                if (BestiaryCatalog.TryParseSkill(_newSkillName, out _))
                {
                    e.skills.Add(new BestiarySkillEntry { skill = _newSkillName.Trim(), level = 1 });
                    _dirty = true;
                }
                else _status = $"Compétence inconnue : '{_newSkillName}'.";
            }
            GUILayout.EndHorizontal();

            // ---- Spécialisations ----
            GUILayout.Label("Spécialisations", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            if (e.specializations == null) e.specializations = new List<string>();
            for (int i = e.specializations.Count - 1; i >= 0; i--)
            {
                GUILayout.BeginHorizontal();
                e.specializations[i] = GUILayout.TextField(e.specializations[i] ?? "");
                if (GUILayout.Button("x", GUILayout.Width(26))) { e.specializations.RemoveAt(i); _dirty = true; }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            _newSpecName = GUILayout.TextField(_newSpecName);
            if (GUILayout.Button("+ Ajouter spé") && !string.IsNullOrWhiteSpace(_newSpecName))
            {
                e.specializations.Add(_newSpecName.Trim());
                _newSpecName = "";
                _dirty = true;
            }
            GUILayout.EndHorizontal();

            // ---- Équipement (noms catalogue Livre VIII) ----
            GUILayout.Label("Équipement (nom exact du catalogue, ex: Épée Métal Standard)", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            if (e.equipment == null) e.equipment = new List<string>();
            for (int i = e.equipment.Count - 1; i >= 0; i--)
            {
                GUILayout.BeginHorizontal();
                string before = e.equipment[i];
                e.equipment[i] = GUILayout.TextField(e.equipment[i] ?? "");
                if (e.equipment[i] != before) _dirty = true;
                bool eq = (i == e.equippedIndex);
                if (GUILayout.Toggle(eq, "Équipé", GUI.skin.button, GUILayout.Width(70)) != eq)
                {
                    e.equippedIndex = eq ? -1 : i;
                    _dirty = true;
                }
                if (GUILayout.Button("x", GUILayout.Width(26)))
                {
                    e.equipment.RemoveAt(i);
                    if (e.equippedIndex == i) e.equippedIndex = -1;
                    else if (e.equippedIndex > i) e.equippedIndex--;
                    _dirty = true;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            _newEquipName = GUILayout.TextField(_newEquipName);
            if (GUILayout.Button("+ Ajouter objet") && !string.IsNullOrWhiteSpace(_newEquipName))
            {
                var def = Killtime.Core.Inventory.ArmoryCatalog.GetByName(_newEquipName.Trim());
                if (def != null)
                {
                    e.equipment.Add(def.Name);
                    if (e.equippedIndex < 0 && def.Type == Killtime.Core.Inventory.ItemType.Weapon)
                        e.equippedIndex = e.equipment.Count - 1;
                    _newEquipName = "";
                    _dirty = true;
                }
                else _status = $"Objet inconnu au catalogue : '{_newEquipName}'.";
            }
            GUILayout.EndHorizontal();

            // ---- Spawn ----
            GUILayout.Label("Spawner en scène", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            GUILayout.BeginHorizontal();
            GUILayout.Label("Q", GUILayout.Width(16));
            if (int.TryParse(GUILayout.TextField(_spawnQ.ToString(), GUILayout.Width(44)), out int q)) _spawnQ = q;
            GUILayout.Label("R", GUILayout.Width(16));
            if (int.TryParse(GUILayout.TextField(_spawnR.ToString(), GUILayout.Width(44)), out int r)) _spawnR = r;
            if (GUILayout.Button("Spawner ENNEMI")) SpawnSelected(false);
            if (GUILayout.Button("Spawner ALLIÉ")) SpawnSelected(true);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🏗️ Instancier PNJ sur la carte en cours")) InstantiateOnCurrentMap();
            GUILayout.EndHorizontal();
            GUILayout.Label("Instancier = spawn live + ajout aux unités de la carte chargée (conservé au reset, inclus à la sauvegarde).", new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Italic });
        }

        private string TextRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            string nv = GUILayout.TextField(value ?? "");
            GUILayout.EndHorizontal();
            if (nv != value) _dirty = true;
            return nv;
        }

        private int IntRow(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            if (GUILayout.Button("-", GUILayout.Width(26))) { value = Mathf.Max(min, value - 1); _dirty = true; }
            GUILayout.Label(value.ToString(), GUILayout.Width(60));
            if (GUILayout.Button("+", GUILayout.Width(26))) { value = Mathf.Min(max, value + 1); _dirty = true; }
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        private void CycleRow(string label, string[] options, Action<string> set, string current)
        {
            int idx = Array.IndexOf(options, current);
            if (idx < 0) idx = 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            if (GUILayout.Button("◀", GUILayout.Width(30))) { idx = (idx - 1 + options.Length) % options.Length; set(options[idx]); _dirty = true; }
            GUILayout.Label(options[idx], GUILayout.Width(140));
            if (GUILayout.Button("▶", GUILayout.Width(30))) { idx = (idx + 1) % options.Length; set(options[idx]); _dirty = true; }
            GUILayout.EndHorizontal();
        }

        // ==================================================================
        private void CreateEntry()
        {
            var all = BestiaryCatalog.GetAll();
            var e = new BestiaryEntry
            {
                id = "nouveau_" + (all.Count + 1),
                name = "Nouvelle Créature",
                category = "Minion",
                rank = "Rang I",
                profile = "PnjSbire",
                species = "Humain",
                equippedIndex = -1
            };
            all.Add(e);
            _selected = e;
            _dirty = true;
            Refresh();
            _status = "Nouvelle entrée créée (pensez à sauvegarder).";
        }

        private void DuplicateSelected()
        {
            if (_selected == null) return;
            var all = BestiaryCatalog.GetAll();
            string json = JsonUtility.ToJson(_selected);
            var copy = JsonUtility.FromJson<BestiaryEntry>(json);
            copy.id = _selected.id + "_copie";
            copy.name = _selected.name + " (copie)";
            all.Add(copy);
            _selected = copy;
            _dirty = true;
            Refresh();
        }

        private void DeleteSelected()
        {
            if (_selected == null) return;
            BestiaryCatalog.GetAll().Remove(_selected);
            _status = $"Entrée '{_selected.id}' supprimée (pensez à sauvegarder).";
            _selected = null;
            _dirty = true;
            Refresh();
        }

        private void SaveOverride()
        {
            if (BestiaryCatalog.SaveOverride(BestiaryCatalog.GetAll(), out string msg))
            {
                _dirty = false;
                Refresh();
            }
            _status = msg;
        }

#if UNITY_EDITOR
        private void ExportResources()
        {
            if (BestiaryCatalog.ExportToResources(BestiaryCatalog.GetAll(), out string msg))
            {
                _dirty = false;
                Refresh();
            }
            _status = msg;
        }
#endif

        private void SpawnSelected(bool asPlayer)
        {
            if (_selected == null) return;
            try
            {
                var unit = TrySpawnLiveUnit(asPlayer, out string spawnMsg);
                if (unit == null) { _status = spawnMsg; return; }
                _status = spawnMsg;
            }
            catch (Exception ex) { _status = $"Spawn impossible : {ex.Message}"; }
        }

        /// <summary>
        /// Instancie l'entrée sélectionnée comme PNJ sur la carte en cours :
        /// spawn live (comme Spawner ENNEMI) + enregistrement dans les unités de la
        /// carte chargée de l'arène si elle existe (conservé au reset, repris à la
        /// sauvegarde puisque celle-ci collecte les unités live).
        /// </summary>
        private void InstantiateOnCurrentMap()
        {
            if (_selected == null) return;
            try
            {
                var unit = TrySpawnLiveUnit(false, out string spawnMsg);
                if (unit == null) { _status = spawnMsg; return; }

                var arena = FindAnyObjectByType<CombatDevArena>();
                var map = arena != null ? arena.CurrentLoadedMap : null;
                if (map == null)
                {
                    _status = spawnMsg + " (aucune carte chargée : sera inclus à la prochaine sauvegarde de carte).";
                    return;
                }

                map.PlacedUnits ??= new List<MapUnitData>();
                map.PlacedUnits.Add(new MapUnitData
                {
                    UnitId = unit.gameObject.name,
                    Sheet = unit.GetOrBuildSheet(),
                    Q = unit.CurrentCoords.Q,
                    R = unit.CurrentCoords.R,
                    IsPlayer = false,
                    currentHealth = unit.Stats.CurrentHealth,
                    currentAP = unit.Stats.CurrentActionPoints,
                    essoufflement = unit.Stats.Essoufflement,
                    activeStatus = (int)unit.Stats.ActiveStatus,
                    statusEffects = unit.Stats.ActiveStatus != StatusEffect.None
                        ? new List<string> { unit.Stats.ActiveStatus.ToString() }
                        : new List<string>()
                });

                _status = $"'{unit.Stats.Name}' instancié en ({unit.CurrentCoords.Q},{unit.CurrentCoords.R}) sur la carte '{map.MapName}' ({map.PlacedUnits.Count} PNJ/avatars).";
            }
            catch (Exception ex) { _status = $"Instanciation impossible : {ex.Message}"; }
        }

        /// <summary>Spawn live partagé : convertit la fiche, valide la case, crée
        /// l'unité et l'enregistre au TurnManager. Retourne null + message si échec.</summary>
        private TacticalUnit TrySpawnLiveUnit(bool asPlayer, out string message)
        {
            message = "";
            if (_selected == null) { message = "Aucune entrée sélectionnée."; return null; }

            var sheet = BestiaryCatalog.ToCharacterSheet(_selected);
            if (sheet == null) { message = "Conversion impossible."; return null; }

            var grid = FindAnyObjectByType<TacticalHexGrid>();
            if (grid == null) { message = "Pas de grille tactique en scène."; return null; }

            var coords = new HexCoordinates(_spawnQ, _spawnR);
            var node = grid.GetNode(coords);
            if (node == null || !node.IsWalkable)
            {
                message = $"Case ({_spawnQ},{_spawnR}) invalide.";
                return null;
            }

            var go = new GameObject($"Bestiary_{sheet.Name.Replace(" ", "_")}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.InitializeFromSheet(sheet, coords, grid, asPlayer);
            unit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);

            var tm = FindAnyObjectByType<TurnManager>();
            tm?.RegisterUnit(unit);

            message = $"'{sheet.Name}' spawné en ({_spawnQ},{_spawnR}) ({(asPlayer ? "allié" : "ennemi")}).";
            return unit;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.Units
{
    public partial class TacticalUnit : MonoBehaviour
    {
        public bool IsCanonEntrave()
        {
            if (_grid == null) return false;
            var weapon = Sheet != null ? Sheet.GetEquippedWeapon() : null;
            bool hasLongWeapon = weapon != null && (weapon.RangeInTiles > 1
                                                    || weapon.AssociatedSkill == SkillType.Ballistique
                                                    || weapon.AssociatedSkill == SkillType.ProjectilesTir
                                                    || weapon.EquipSlot == Killtime.Core.Inventory.ItemEquipSlot.TwoHands);
            if (!hasLongWeapon) return false;

            var allUnits = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < allUnits.Count; i++)
            {
                var other = allUnits[i];
                if (other == null || other == this || other.Stats == null || !other.Stats.CanDefendActively()) continue;
                if (other.IsPlayerControlled != this.IsPlayerControlled)
                {
                    if (TitanFootprint.MinDistanceBetweenUnits(this.CurrentCoords, this._footprintType, other.CurrentCoords, other._footprintType) <= 1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void NotifyInventoryChanged(bool saveToDisk = true)
        {
            var visual = GetComponent<TacticalUnitVisual>();
            visual?.RefreshEquippedWeaponVisual();
            // Barrière : suit le champ porté (crédite le gain immédiat).
            if (Stats != null) Stats.RecalcShieldMax();

            if (Sheet != null && saveToDisk)
            {
                CharacterStorageService.SaveCharacter(Sheet);
            }
        }

        public void IntroduceTo(TacticalUnit other, RelationshipLinkType defaultLink = RelationshipLinkType.NeutreInconnu, string location = "Terrain Tactique", string context = "Rencontre Tactique")
        {
            if (other == null || other == this) return;
            var sheetA = GetOrBuildSheet();
            var sheetB = other.GetOrBuildSheet();
            if (sheetA == null || sheetB == null) return;
            CharacterRelationshipStorageService.IntroduceCharacters(sheetA, sheetB, defaultLink, location, context);
        }

        private void Awake()
        {
            if (Stats == null)
            {
                var attributes = new Attributes(
                    @for: _force, 
                    agi: _agilite, 
                    con: _constitution, 
                    rap: _rapidite, 
                    @int: _intelligence, 
                    eru: _erudition, 
                    cha: _charisme, 
                    ins: _instinct, 
                    mag: _magie
                );
                Stats = new CharacterStats(_unitName, attributes, _baseArmor, Sheet);
            }

            if (GetComponent<TacticalUnitVisual>() == null)
            {
                gameObject.AddComponent<TacticalUnitVisual>();
            }

            if (GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 0.9f, 0f);
                col.radius = 0.35f;
                col.height = 1.8f;
            }
        }

        private void OnEnable()
        {
            TacticalUnitRegistry.Register(this);
        }

        private void OnDestroy()
        {
            TacticalUnitRegistry.Unregister(this);

            if (_grid != null)
            {
                var list = OccupiedCoords;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    var node = _grid.GetNode(c);
                    if (node != null && !IsAnyOtherUnitAt(c))
                    {
                        node.IsOccupied = false;
                    }
                }
            }
        }

        /// <summary>
        /// Initialisation complète depuis une fiche de personnage (PJ ou PNJ).
        /// Appelée par le CharacterDevWindow lors de l'insertion sur la carte.
        /// </summary>
        public void InitializeFromSheet(CharacterSheet sheet, HexCoordinates coords, TacticalHexGrid grid, bool isPlayerControlled)
        {
            _grid = grid;
            _modelPrefabName = sheet.ModelPrefabName;
            _footprintType = sheet.Footprint;

            var effective = sheet.GetEffectiveAttributes();
            ConfigureStats(sheet.Name, effective, sheet.BaseArmor, isPlayerControlled, sheet.ModelPrefabName);

            // ConfigureStats recrée une fiche vierge : on y recopie la progression
            // de la fiche source (entraînements, spécialisations, sorts, XP).
            // Sans ça, les entraînements (ex: Mains Nues +3) et spés (ex: Arts Martiaux)
            // de l'avatar sont perdus sur la carte et ignorés en combat.
            // Note : on ne recopie NI l'espèce NI les attributs de base (effective déjà
            // appliquée ci-dessus, sinon les modificateurs raciaux seraient comptés deux fois).
            if (Sheet != null && sheet != null && !object.ReferenceEquals(Sheet, sheet))
            {
                Sheet.SheetId = sheet.SheetId;
                Sheet.Skills.Clear();
                for (int i = 0; i < sheet.Skills.Count; i++)
                {
                    var src = sheet.Skills[i];
                    if (src != null)
                    {
                        Sheet.Skills.Add(new SkillProgressionEntry(src.Skill, src.TrainingLevel));
                    }
                }

                Sheet.UnlockedSpecializations.Clear();
                if (sheet.UnlockedSpecializations != null)
                {
                    Sheet.UnlockedSpecializations.AddRange(sheet.UnlockedSpecializations);
                }

                Sheet.LearnedSpells.Clear();
                if (sheet.LearnedSpells != null)
                {
                    Sheet.LearnedSpells.AddRange(sheet.LearnedSpells);
                }

                Sheet.Inventory.Clear();
                if (sheet.Inventory != null)
                {
                    for (int i = 0; i < sheet.Inventory.Count; i++)
                    {
                        var it = sheet.Inventory[i];
                        if (it != null) Sheet.Inventory.Add(it.Clone());
                    }
                }

                Sheet.AvailableXP = sheet.AvailableXP;
                Sheet.TotalEarnedXP = sheet.TotalEarnedXP;
                Sheet.TotalSpentXP = sheet.TotalSpentXP;
                Sheet.FreeTrainingsUsed = sheet.FreeTrainingsUsed;
                Sheet.CreditsCE = sheet.CreditsCE;
                Sheet.BaseArmor = sheet.BaseArmor;
                Sheet.LoreNotes = sheet.LoreNotes;
                Sheet.Species = sheet.Species;
                Sheet.Gender = sheet.Gender;
                Sheet.Age = sheet.Age;
                Sheet.Profile = sheet.Profile;
                Sheet.ActiveClassId = sheet.ActiveClassId;
                Sheet.BuildGuideEnabled = sheet.BuildGuideEnabled;
                Sheet.Footprint = sheet.Footprint;
                if (sheet.AttributeUpgradesPurchased != null)
                {
                    Sheet.AttributeUpgradesPurchased = (int[])sheet.AttributeUpgradesPurchased.Clone();
                }
            }

            _footprintType = Sheet != null ? Sheet.Footprint : sheet.Footprint;
            if (Stats != null)
            {
                Stats.IsPlayerControlled = isPlayerControlled;
                Stats.IsSurvivor = (_aiPersonality == AI.AIPersonality.Survivor);
            }
            InitializePosition(coords, grid);
            // Filet de sécurité : si l'appelant n'a pas résolu une case libre
            // (ex: chargement direct sans arène), on relocalise au lieu d'empiler.
            if (!_hasPosition && grid != null && grid.TryFindNearestFreeCell(coords, out var sheetFallback, 8))
            {
                InitializePosition(sheetFallback, grid);
                if (_hasPosition && !sheetFallback.Equals(coords))
                {
                    Debug.Log($"[TacticalUnit] '{_unitName}' relocalisé en {sheetFallback} (demandé {coords} occupé).");
                }
            }

            var visual = GetComponent<TacticalUnitVisual>();
            if (visual != null)
            {
                if (!string.IsNullOrEmpty(sheet.ModelPrefabName))
                {
                    visual.ApplyCustomModel(sheet.ModelPrefabName);
                }
                else
                {
                    visual.BuildProceduralAvatar();
                }

                if (isPlayerControlled)
                {
                    visual.SetColor(new Color(0.15f, 0.45f, 0.85f), new Color(0.0f, 0.95f, 1.0f));
                }
                else
                {
                    visual.SetColor(new Color(0.85f, 0.25f, 0.2f), new Color(1.0f, 0.75f, 0.1f));
                }
            }
        }

        public void ConfigureStats(string unitName, Attributes attributes, int baseArmor, bool isPlayer, string modelPrefab = "")
        {
            _unitName = unitName;
            _force = attributes.Force;
            _agilite = attributes.Agilite;
            _constitution = attributes.Constitution;
            _rapidite = attributes.Rapidite;
            _intelligence = attributes.Intelligence;
            _erudition = attributes.Erudition;
            _charisme = attributes.Charisme;
            _instinct = attributes.Instinct;
            _magie = attributes.Magie;
            _baseArmor = baseArmor;
            _isPlayerControlled = isPlayer;
            if (!string.IsNullOrEmpty(modelPrefab))
            {
                _modelPrefabName = modelPrefab;
            }

            Sheet = new CharacterSheet
            {
                Name = unitName,
                BaseAttributes = attributes,
                BaseArmor = baseArmor,
                Profile = isPlayer ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                ModelPrefabName = _modelPrefabName
            };

            Stats = new CharacterStats(unitName, attributes, baseArmor, Sheet);
            Stats.IsPlayerControlled = isPlayer;
            Stats.IsSurvivor = (_aiPersonality == AI.AIPersonality.Survivor);

            // Backstop EditMode/tests : OnEnable ne se déclenche pas sur les objets
            // créés par AddComponent hors Play (vérifié par sonde LifecycleProbe) —
            // l'enregistrement explicite ici garantit que toute unité configurée
            // est visible du registre. Doublon sans effet en Play (Register ignore).
            TacticalUnitRegistry.Register(this);
        }

        public CharacterSheet GetOrBuildSheet()
        {
            if (Sheet == null)
            {
                var attrs = Stats != null 
                    ? Stats.Attributes 
                    : new Attributes(_force, _agilite, _constitution, _rapidite, _intelligence, _erudition, _charisme, _instinct, _magie);

                Sheet = new CharacterSheet
                {
                    Name = _unitName,
                    BaseAttributes = attrs,
                    BaseArmor = _baseArmor,
                    Profile = _isPlayerControlled ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                    ModelPrefabName = _modelPrefabName
                };
            }
            // Maintient le lien fiche ↔ stats : la fiche affichée/éditée doit être
            // celle lue par GetSkillDie / HasSpecialization en combat.
            if (Stats != null && !object.ReferenceEquals(Stats.Sheet, Sheet))
            {
                Stats.Sheet = Sheet;
            }
            return Sheet;
        }

    }
}

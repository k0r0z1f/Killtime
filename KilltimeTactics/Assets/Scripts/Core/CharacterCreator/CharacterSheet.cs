using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Arcanotech;
using Killtime.Core.Inventory;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Fiche technique intégrale d'un protagoniste (PJ, PNJ régulier ou sbire).
    /// Sérialisable en JSON pour stockage disque ou transmission WebGL.
    /// </summary>
    [Serializable]
    public class CharacterSheet
    {
        public string SheetId = Guid.NewGuid().ToString("N");
        public string Name = "Nouveau Personnage";
        public int Age = 25;
        public string Gender = "Indéterminé";
        public SpeciesType Species = SpeciesType.Humain;
        public CharacterProfileType Profile = CharacterProfileType.HerosPJ;
        public string ModelPrefabName = "";
        public string LoreNotes = "";

        public Attributes BaseAttributes = new Attributes(@for: 5, agi: 3, con: 4, rap: 3, @int: 2, eru: 3, cha: 1, ins: 3, mag: 0, vision: 3, ouie: 3, miracle: 1);
        public int BaseArmor = 1;
        public int AvailableXP = 0;
        public int TotalEarnedXP = 0;

        /// <summary>
        /// XP TOTAL du personnage (Livre I §5.5) : somme de tous les XP DÉPENSÉS
        /// en améliorations (entraînements, spécialisations, sorts, attributs),
        /// montants réellement prélevés (surcoût x2 inclus). N'inclut JAMAIS
        /// les banques (réserves liées + XP libre) non encore dépensées,
        /// ni les entraînements gratuits d'Érudition (0 XP dépensé).
        /// C'est la mesure officielle de puissance / rang du personnage.
        /// </summary>
        public int TotalSpentXP = 0;

        /// <summary>
        /// Entraînements gratuits d'Érudition déjà consommés (Livre I, Étape 5).
        /// Budget = Érudition effective (espèce incluse) ; restants = budget − consommés.
        /// Chaque +1 Érudition ouvre +1 entraînement gratuit. Les gratuits ne coûtent
        /// aucun XP et ne comptent jamais dans l'XP total (<see cref="TotalSpentXP"/>).
        /// </summary>
        public int FreeTrainingsUsed = 0;
        public int CreditsCE = 8000;

        public List<SkillProgressionEntry> Skills = new();
        public List<string> UnlockedSpecializations = new();
        public List<NythariteSpell> LearnedSpells = new();
        public List<InventoryItem> Inventory = new();

        public CharacterSheet()
        {
            InitializeDefaultSkills();
        }

        public void InitializeDefaultSkills()
        {
            Skills.Clear();
            foreach (SkillType skill in Enum.GetValues(typeof(SkillType)))
            {
                Skills.Add(new SkillProgressionEntry(skill, 0));
            }
        }

        public SkillProgressionEntry GetSkill(SkillType type)
        {
            var entry = Skills.Find(s => s.Skill == type);
            if (entry == null)
            {
                entry = new SkillProgressionEntry(type, 0);
                Skills.Add(entry);
            }
            return entry;
        }

        /// <summary>
        /// Total des XP en banque (non dépensés) : XP libre + toutes réserves liées.
        /// Distinct de <see cref="TotalSpentXP"/> (XP total = dépensé, Livre I §5.5).
        /// </summary>
        public int GetBankedXP()
        {
            int total = AvailableXP;
            if (Skills != null)
                for (int i = 0; i < Skills.Count; i++)
                    if (Skills[i] != null) total += Mathf.Max(0, Skills[i].ReserveXP);
            return Mathf.Max(0, total);
        }

        /// <summary>
        /// Total des réserves liées (hors XP libre).
        /// </summary>
        public int GetLinkedReserveTotal()
        {
            int total = 0;
            if (Skills != null)
                for (int i = 0; i < Skills.Count; i++)
                    if (Skills[i] != null) total += Mathf.Max(0, Skills[i].ReserveXP);
            return total;
        }

        /// <summary>
        /// Budget d'entraînements gratuits (Livre I, Étape 5) = Érudition effective
        /// (modificateurs d'espèce inclus, plancher 0).
        /// </summary>
        public int GetFreeTrainingBudget()
        {
            return Mathf.Max(0, GetEffectiveAttributes().Erudition);
        }

        /// <summary>
        /// Entraînements gratuits restants = budget − déjà consommés (plancher 0).
        /// </summary>
        public int GetFreeTrainingsRemaining()
        {
            return Mathf.Max(0, GetFreeTrainingBudget() - Mathf.Max(0, FreeTrainingsUsed));
        }

        /// <summary>
        /// Total des niveaux d'entraînement toutes compétences confondues.
        /// </summary>
        public int GetTotalTrainingLevels()
        {
            int total = 0;
            if (Skills != null)
                for (int i = 0; i < Skills.Count; i++)
                    if (Skills[i] != null) total += Mathf.Max(0, Skills[i].TrainingLevel);
            return total;
        }

        public InventoryItem GetEquippedWeapon()
        {
            if (Inventory == null) return null;
            return Inventory.Find(i => i != null && i.IsEquipped && i.Type == ItemType.Weapon);
        }

        public bool EquipItem(string itemId)
        {
            if (Inventory == null) return false;
            var item = Inventory.Find(i => i != null && i.ItemId == itemId);
            if (item == null) return false;

            if (item.Type == ItemType.Weapon)
            {
                for (int i = 0; i < Inventory.Count; i++)
                {
                    if (Inventory[i] != null && Inventory[i].Type == ItemType.Weapon)
                    {
                        Inventory[i].IsEquipped = false;
                    }
                }
            }

            item.IsEquipped = true;
            return true;
        }

        public bool UnequipItem(string itemId)
        {
            if (Inventory == null) return false;
            var item = Inventory.Find(i => i != null && i.ItemId == itemId);
            if (item == null) return false;
            item.IsEquipped = false;
            return true;
        }

        public void AddItem(InventoryItem item)
        {
            if (item == null) return;
            Inventory ??= new List<InventoryItem>();
            Inventory.Add(item);
        }

        public bool RemoveItem(string itemId)
        {
            if (Inventory == null) return false;
            return Inventory.RemoveAll(i => i != null && i.ItemId == itemId) > 0;
        }

        public float GetTotalWeightKg()
        {
            float total = 0f;
            if (Inventory == null) return 0f;
            for (int i = 0; i < Inventory.Count; i++)
                if (Inventory[i] != null) total += Inventory[i].WeightKg * Mathf.Max(1, Inventory[i].Quantity);
            return total;
        }

        public bool CanAfford(int priceCE) => CreditsCE >= priceCE;

        public bool SpendCredits(int amount)
        {
            if (amount < 0) return false;
            if (CreditsCE < amount) return false;
            CreditsCE -= amount;
            return true;
        }

        public void EarnCredits(int amount)
        {
            if (amount < 0) return;
            CreditsCE += amount;
        }

        /// <summary>
        /// Applique les modificateurs de l'espèce aux attributs de base.
        /// </summary>
        public Attributes GetEffectiveAttributes()
        {
            var (dFor, dAgi, dCon, dRap, dInt, dEru, dCha, dIns, minSense) = SpeciesRules.GetModifiers(Species);
            return new Attributes(
                @for: Mathf.Clamp(BaseAttributes.Force + dFor, 1, 10),
                agi: Mathf.Clamp(BaseAttributes.Agilite + dAgi, 1, 10),
                con: Mathf.Clamp(BaseAttributes.Constitution + dCon, 1, 10),
                rap: Mathf.Clamp(BaseAttributes.Rapidite + dRap, 1, 10),
                @int: Mathf.Clamp(BaseAttributes.Intelligence + dInt, 1, 10),
                eru: Mathf.Clamp(BaseAttributes.Erudition + dEru, 1, 10),
                cha: Mathf.Clamp(BaseAttributes.Charisme + dCha, 1, 10),
                ins: Mathf.Clamp(BaseAttributes.Instinct + dIns, 1, 10),
                mag: Mathf.Clamp(BaseAttributes.Magie, 0, 10),
                vision: Mathf.Clamp(Mathf.Max(minSense, BaseAttributes.Vision), 1, 6),
                ouie: Mathf.Clamp(Mathf.Max(minSense, BaseAttributes.Ouie), 1, 6),
                miracle: BaseAttributes.PointsMiracle
            );
        }

        /// <summary>
        /// Génère le composant CharacterStats prêt pour le combat.
        /// </summary>
        public CharacterStats ToCombatStats()
        {
            var effective = GetEffectiveAttributes();
            var stats = new CharacterStats(Name, effective, BaseArmor);
            stats.Sheet = this;
            return stats;
        }
    }
}